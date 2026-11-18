using System;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Fixed Game View presets and observed render-target dimensions. Unity's
    /// undocumented Editor APIs stay here and fail explicitly if they change.
    /// </summary>
    internal static class GameViewSizeControl
    {
        // Unity clamps fixed presets to at least 10 pixels and Game View render
        // targets to at most 8192 (also subject to GPU and VRAM limits).
        internal const int MinDimension = 10;
        internal const int MaxDimension = 8192;
        internal const string OwnershipKeyPrefix = "MCPForUnity.GameViewSize.";
        private const double ReadbackTimeoutSeconds = 5;
        private static bool _settingSize;

        internal static object Get()
        {
            try
            {
                var api = new GameViewApi();
                var view = api.FindView();
                return new SuccessResponse("Current Game View size. render_size is the allocated render target, not the preset or window size.",
                    api.Read(view));
            }
            catch (Exception ex)
            {
                return Failure(ex, false);
            }
        }

        internal static object Set(ToolParams parameters)
        {
            foreach (string name in new[] { "width", "height" })
            {
                var token = parameters.GetRaw(name);
                if (token?.Type != JTokenType.Integer || !parameters.GetInt(name).HasValue
                    || parameters.GetInt(name).Value < MinDimension || parameters.GetInt(name).Value > MaxDimension)
                    return new ErrorResponse($"{name} must be an integer between {MinDimension} and {MaxDimension}.");
            }
            if (_settingSize)
                return new ErrorResponse("game_view_size_busy", new { message = "Another Game View size change is waiting for readback." });

            bool changed = false;
            try
            {
                var api = new GameViewApi();
                var view = api.FindView();
                int width = parameters.GetInt("width").Value;
                int height = parameters.GetInt("height").Value;
                object groupType = api.CurrentGroupType;
                _settingSize = true;
                changed = true;
                int index = api.SetOwnedPreset(groupType, width, height);
                api.Select(view, index);
                return WaitForReadback(api, view, groupType, index, width, height);
            }
            catch (Exception ex)
            {
                _settingSize = false;
                return Failure(ex, changed);
            }
        }

        /// <summary>Wait for Unity to allocate the selected render target without advancing gameplay or taking focus.</summary>
        private static Task<object> WaitForReadback(GameViewApi api, EditorWindow view, object groupType,
            int selectedIndex, int width, int height)
        {
            var completion = new TaskCompletionSource<object>();
            double started = EditorApplication.timeSinceStartup;
            int settledUpdates = 0;
            EditorApplication.CallbackFunction update = null;
            void Finish(object result)
            {
                EditorApplication.update -= update;
                AssemblyReloadEvents.beforeAssemblyReload -= Interrupted;
                EditorApplication.quitting -= Interrupted;
                _settingSize = false;
                completion.TrySetResult(result);
            }
            void Interrupted()
            {
                Finish(new ErrorResponse("game_view_size_interrupted", new
                {
                    preset_may_have_changed = true,
                    message = "Editor shutdown or assembly reload interrupted readback. Query the Game View size after reconnecting."
                }));
            }
            update = () =>
            {
                try
                {
                    if (view == null || !Equals(api.CurrentGroupType, groupType)
                        || api.SelectedIndex(view) != selectedIndex)
                    {
                        Finish(new ErrorResponse("game_view_size_changed", new
                        {
                            preset_may_have_changed = true,
                            message = "The Game View closed, its platform group changed, or another preset was selected while waiting."
                        }));
                        return;
                    }
                    JObject state = api.Read(view, width, height);
                    if (state.Value<string>("mode") != "FixedResolution"
                        || (int)state["selected_size"]["width"] != width
                        || (int)state["selected_size"]["height"] != height)
                    {
                        state["preset_may_have_changed"] = true;
                        state["message"] = "The selected preset was edited while waiting for render-target readback.";
                        Finish(new ErrorResponse("game_view_size_changed", state));
                        return;
                    }
                    settledUpdates = state.Value<bool>("settled") ? settledUpdates + 1 : 0;
                    if (settledUpdates >= 2)
                    {
                        bool matches = state.Value<bool>("matches_requested");
                        Finish(new SuccessResponse(matches
                            ? "Fixed Game View size applied and render-target dimensions verified."
                            : "Preset applied, but Unity limited the render-target dimensions. Check render_size before verifying the layout.", state));
                    }
                    else if (EditorApplication.timeSinceStartup - started >= ReadbackTimeoutSeconds)
                    {
                        state["preset_may_have_changed"] = true;
                        state["message"] = "The preset was applied, but render-target readback did not settle. Make the Game View visible and query its size again.";
                        Finish(new ErrorResponse("game_view_size_readback_timeout", state));
                    }
                    else
                    {
                        view.Repaint();
                    }
                }
                catch (Exception ex)
                {
                    Finish(Failure(ex, true));
                }
            };
            EditorApplication.update += update;
            AssemblyReloadEvents.beforeAssemblyReload += Interrupted;
            EditorApplication.quitting += Interrupted;
            view.Repaint();
            return completion.Task;
        }

        private static ErrorResponse Failure(Exception ex, bool changed)
        {
            if (ex is TargetInvocationException invocation && invocation.InnerException != null)
                ex = invocation.InnerException;
            return new ErrorResponse(ex is MissingMemberException || ex is TypeLoadException
                ? "game_view_size_unsupported" : "game_view_size_unavailable", new
            {
                message = ex.Message,
                unity_version = Application.unityVersion,
                preset_may_have_changed = changed
            });
        }

        /// <summary>Resolves the complete internal API before changing any preset.</summary>
        internal sealed class GameViewApi
        {
            private const BindingFlags Instance = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            private readonly Type _viewType;
            private readonly object _sizes;
            private readonly PropertyInfo _currentGroupType, _selectedIndex, _targetRenderSize;
            private readonly PropertyInfo _baseText, _width, _height, _sizeType;
            private readonly FieldInfo _targetTexture;
            private readonly MethodInfo _getGroup, _getSize, _getTotalCount, _getBuiltinCount, _addSize, _save;
            private readonly MethodInfo _updateZoom;
            private readonly PropertyInfo _targetSize;
            private readonly ConstructorInfo _newSize;
            private readonly object _fixedResolution;

            internal GameViewApi()
            {
                var assembly = typeof(EditorWindow).Assembly;
                _viewType = assembly.GetType("UnityEditor.GameView", true);
                var sizesType = assembly.GetType("UnityEditor.GameViewSizes", true);
                var groupType = assembly.GetType("UnityEditor.GameViewSizeGroup", true);
                var sizeType = assembly.GetType("UnityEditor.GameViewSize", true);
                var kindType = assembly.GetType("UnityEditor.GameViewSizeType", true);
                var playModeViewType = assembly.GetType("UnityEditor.PlayModeView", true);
                var singleton = sizesType.BaseType.GetProperty("instance", BindingFlags.Static | BindingFlags.Public);
                if (singleton == null) throw new MissingMemberException(sizesType.FullName, "instance");
                _sizes = singleton.GetValue(null);
                _currentGroupType = RequireProperty(sizesType, "currentGroupType");
                _selectedIndex = RequireProperty(_viewType, "selectedSizeIndex", true);
                _targetRenderSize = RequireProperty(_viewType, "targetRenderSize");
                _targetSize = RequireProperty(playModeViewType, "targetSize", true);
                _baseText = RequireProperty(sizeType, "baseText", true);
                _width = RequireProperty(sizeType, "width", true);
                _height = RequireProperty(sizeType, "height", true);
                _sizeType = RequireProperty(sizeType, "sizeType");
                _targetTexture = playModeViewType.GetField("m_TargetTexture", Instance);
                if (_targetTexture == null || _targetTexture.FieldType != typeof(RenderTexture))
                    throw new MissingMemberException(playModeViewType.FullName, "m_TargetTexture");
                _getGroup = RequireMethod(sizesType, "GetGroup", _currentGroupType.PropertyType);
                _getSize = RequireMethod(groupType, "GetGameViewSize", typeof(int));
                _getTotalCount = RequireMethod(groupType, "GetTotalCount");
                _getBuiltinCount = RequireMethod(groupType, "GetBuiltinCount");
                _addSize = RequireMethod(groupType, "AddCustomSize", sizeType);
                _save = RequireMethod(sizesType, "SaveToHDD");
                _updateZoom = RequireMethod(_viewType, "UpdateZoomAreaAndParent");
                _newSize = sizeType.GetConstructor(new[] { kindType, typeof(int), typeof(int), typeof(string) });
                if (_newSize == null) throw new MissingMethodException(sizeType.FullName, ".ctor");
                _fixedResolution = Enum.Parse(kindType, "FixedResolution");
            }

            internal static PropertyInfo RequireProperty(Type type, string name, bool writable = false)
            {
                var property = type.GetProperty(name, Instance);
                if (property?.GetGetMethod(true) == null || (writable && property.GetSetMethod(true) == null))
                    throw new MissingMemberException(type.FullName, name);
                return property;
            }

            private static MethodInfo RequireMethod(Type type, string name, params Type[] arguments)
            {
                return type.GetMethod(name, Instance, null, arguments, null)
                    ?? throw new MissingMethodException(type.FullName, name);
            }

            internal object CurrentGroupType => _currentGroupType.GetValue(_sizes);
            internal int SelectedIndex(EditorWindow view) => (int)_selectedIndex.GetValue(view);

            internal EditorWindow FindView()
            {
                if (Application.isBatchMode)
                    throw new InvalidOperationException("Game View size actions require a graphical Editor, not batch mode.");
                var views = UnityEngine.Resources.FindObjectsOfTypeAll(_viewType).Cast<EditorWindow>().ToArray();
                if (EditorWindow.focusedWindow != null && _viewType.IsInstanceOfType(EditorWindow.focusedWindow))
                    return EditorWindow.focusedWindow;
                if (views.Length == 1) return views[0];
                throw new InvalidOperationException(views.Length == 0
                    ? "Open a Game View before reading or setting its size."
                    : "Multiple Game Views are open. Focus the one to read or resize.");
            }

            /// <summary>Change only the exact preset recorded as ours in shared Editor preferences.</summary>
            internal int SetOwnedPreset(object groupType, int width, int height)
            {
                object group = _getGroup.Invoke(_sizes, new[] { groupType });
                string key = OwnershipKeyPrefix + groupType;
                JObject ownership = null;
                try { ownership = JObject.Parse(EditorPrefs.GetString(key, "{}")); }
                catch (Newtonsoft.Json.JsonException) { /* A damaged ownership record must not authorize overwriting a preset. */ }
                int total = (int)_getTotalCount.Invoke(group, null);
                int builtin = (int)_getBuiltinCount.Invoke(group, null);
                int index = -1;
                object ownedSize = null;
                for (int i = builtin; i < total; i++)
                {
                    object size = _getSize.Invoke(group, new object[] { i });
                    if (ownership != null && (string)_baseText.GetValue(size) == (string)ownership["name"]
                        && JToken.DeepEquals(JToken.FromObject(_width.GetValue(size)), ownership["width"])
                        && JToken.DeepEquals(JToken.FromObject(_height.GetValue(size)), ownership["height"])
                        && Equals(_sizeType.GetValue(size), _fixedResolution))
                    {
                        if (ownedSize != null)
                            throw new InvalidOperationException("The MCP-owned preset was duplicated. Remove the duplicate in the Game View menu before resizing.");
                        ownedSize = size;
                        index = i;
                    }
                }
                string name = $"MCP {width}x{height} [{Guid.NewGuid().ToString("N").Substring(0, 8)}]";
                if (ownedSize == null)
                {
                    ownedSize = _newSize.Invoke(new object[] { _fixedResolution, width, height, name });
                    index = total;
                    _addSize.Invoke(group, new[] { ownedSize });
                }
                else
                {
                    if ((int)_width.GetValue(ownedSize) == width && (int)_height.GetValue(ownedSize) == height)
                        return index;
                    _width.SetValue(ownedSize, width);
                    _height.SetValue(ownedSize, height);
                    _baseText.SetValue(ownedSize, name);
                }
                // Track the exact name and dimensions, not a prefix or a shifting index.
                // If a user edits that preset later, preserve it and create a new owned entry.
                EditorPrefs.SetString(key, new JObject { ["name"] = name, ["width"] = width, ["height"] = height }.ToString());
                _save.Invoke(_sizes, null);
                return index;
            }

            internal void Select(EditorWindow view, int index)
            {
                // Unity's normal selection callback skips refresh when the index is
                // unchanged, which is precisely the case when reusing our preset.
                _selectedIndex.SetValue(view, index);
                _updateZoom.Invoke(view, null);
                _targetSize.SetValue(view, _targetRenderSize.GetValue(view));
                view.Repaint();
            }

            internal JObject Read(EditorWindow view, int? width = null, int? height = null)
            {
                object groupType = CurrentGroupType;
                int index = SelectedIndex(view);
                object group = _getGroup.Invoke(_sizes, new[] { groupType });
                object size = _getSize.Invoke(group, new object[] { index });
                Vector2 target = (Vector2)_targetRenderSize.GetValue(view);
                var texture = _targetTexture.GetValue(view) as RenderTexture;
                var targetSize = Dimensions((int)Math.Round(target.x, MidpointRounding.AwayFromZero),
                    (int)Math.Round(target.y, MidpointRounding.AwayFromZero));
                JObject renderSize = texture != null && texture.IsCreated() ? Dimensions(texture.width, texture.height) : null;
                bool settled = renderSize != null && JToken.DeepEquals(renderSize, targetSize);
                JObject requested = width.HasValue ? Dimensions(width.Value, height.Value) : null;
                return new JObject
                {
                    ["window_id"] = view.GetInstanceID(),
                    ["platform_group"] = groupType.ToString(),
                    ["selected_index"] = index,
                    ["preset_name"] = (string)_baseText.GetValue(size),
                    ["mode"] = _sizeType.GetValue(size).ToString(),
                    ["selected_size"] = Dimensions((int)_width.GetValue(size), (int)_height.GetValue(size)),
                    ["requested_size"] = requested,
                    ["target_size"] = targetSize,
                    ["render_size"] = renderSize,
                    ["settled"] = settled,
                    ["matches_requested"] = requested == null ? JValue.CreateNull() : new JValue(settled && JToken.DeepEquals(renderSize, requested))
                };
            }

            private static JObject Dimensions(int width, int height) => new JObject { ["width"] = width, ["height"] = height };
        }
    }
}
