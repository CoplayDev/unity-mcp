using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace MCPForUnity.Editor.Tools
{
    [InitializeOnLoad]
    [McpForUnityTool("manage_addressables", AutoRegister = false, Group = "core",
        RequiresPolling = true, PollAction = "build_status", MaxPollSeconds = 1800)]
    public static partial class ManageAddressables
    {
        private const string PackageName = "com.unity.addressables";
        private static bool _refreshAfterUndo;

        static ManageAddressables() => Undo.undoRedoPerformed += () => _refreshAfterUndo = true;
        private static readonly string[] Actions =
        {
            "ping", "get_settings", "group_list", "entry_list", "entry_find",
            "label_list", "profile_list", "profile_get", "group_create", "group_remove",
            "entry_add", "entry_remove", "entry_move", "entry_set_address", "label_add", "label_remove",
            "label_set", "profile_set", "profile_set_active", "set_settings",
            "build_content", "build_update", "build_clean", "build_status"
        };

        public static object HandleCommand(JObject parameters)
        {
            try
            {
                if (parameters == null)
                    return new ErrorResponse("Parameters cannot be null.");
                var p = new ToolParams(parameters);
                string action = ReadString(p, "action", true).ToLowerInvariant();
                if (!Actions.Contains(action))
                    return new ErrorResponse($"Unknown action '{action}'. Valid actions: {string.Join(", ", Actions)}");
                if (action == "build_status") return AddressablesBuildJobs.Status(ReadString(p, "job_id"));
                if (AddressablesBuildJobs.IsBusy && !new[] { "ping", "get_settings", "group_list", "entry_list",
                    "entry_find", "label_list", "profile_list", "profile_get" }.Contains(action))
                    return new ErrorResponse("ADDRESSABLES_BUILD_BUSY", new { message = "Wait for the active content operation before changing configuration." });

                var package = PackageInfo.GetAllRegisteredPackages()
                    .FirstOrDefault(info => info.name == PackageName);
                // Resolve the public Editor API without making Addressables a mandatory package reference.
                var defaultType = Type.GetType(
                    "UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
                var getSettings = defaultType?.GetMethod("GetSettings", new[] { typeof(bool) });
                // GetSettings(false) can still migrate legacy config. Refuse that path rather than write during a query.
                bool legacy = !EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>(
                    "com.unity.addressableassets", out _)
                    && EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>("AddressableAssetSettings", out _);
                object settings = legacy ? null : getSettings?.Invoke(null, new object[] { false });
                if (settings != null && _refreshAfterUndo)
                {
                    // Undo restores serialized entries but native 1.x lookup/hash caches can retain removed entries.
                    var eventType = settings.GetType().GetNestedType("ModificationEvent");
                    foreach (var group in Items(Read(settings, "groups")))
                        Invoke(group, "SetDirty", new[] { eventType, typeof(object), typeof(bool), typeof(bool) },
                            Enum.Parse(eventType, "BatchModification"), null, false, false);
                    _refreshAfterUndo = false;
                }
                if (action == "ping")
                    return new SuccessResponse("Addressables availability.", new
                    {
                        installed = package != null,
                        package_version = package?.version,
                        api_available = getSettings != null,
                        settings_exists = settings != null,
                        migration_required = legacy,
                        supported_actions = getSettings != null ? Actions : Array.Empty<string>()
                    });
                if (package == null)
                    return new ErrorResponse("ADDRESSABLES_NOT_INSTALLED", new { package = PackageName });
                if (getSettings == null)
                    return new ErrorResponse("ADDRESSABLES_API_UNSUPPORTED", new { package_version = package.version });
                if (legacy)
                    return new ErrorResponse("ADDRESSABLES_MIGRATION_REQUIRED",
                        new { message = "Open Addressables in Unity to migrate legacy settings explicitly." });
                if (action == "set_settings")
                {
                    var updates = ValidateSettings(p, defaultType.Assembly.GetType(
                        "UnityEditor.AddressableAssets.Settings.AddressableAssetSettings"), settings);
                    if (settings == null && ReadBool(p, "initialize"))
                        settings = getSettings.Invoke(null, new object[] { true });
                    if (settings != null) return SetSettings(settings, updates);
                }
                if (settings == null)
                    return new ErrorResponse("ADDRESSABLES_SETTINGS_NOT_FOUND",
                        new { message = "Create Addressables settings explicitly before querying them." });
                if (action == "build_content" || action == "build_update" || action == "build_clean")
                    return AddressablesBuildJobs.Start(settings, action, ReadString(p, "content_state_path"));

                var groups = Items(Read(settings, "groups")).ToList();
                switch (action)
                {
                    case "get_settings":
                        return new SuccessResponse("Addressables settings.", new
                        {
                            package_version = package.version,
                            settings_path = AssetDatabase.GetAssetPath((UnityEngine.Object)settings),
                            active_profile_id = SerializedString(settings, "m_ActiveProfileId"),
                            group_count = groups.Count,
                            values = SettingFields.Where(field => settings.GetType().GetProperty(field.Value) != null)
                                .ToDictionary(field => field.Key, field => ReadSetting(settings, field.Key, field.Value))
                        });
                    case "group_list":
                        return Page(groups.Select(group => new JObject
                        {
                            ["guid"] = SerializedString(group, "m_GUID"),
                            ["name"] = SerializedString(group, "m_GroupName"),
                            ["entry_count"] = Items(Read(group, "entries")).Count(),
                            ["read_only"] = Convert.ToBoolean(Read(group, "ReadOnly"))
                        }).OrderBy(row => (string)row["guid"], StringComparer.Ordinal), p, action);
                    case "entry_list":
                        string groupSelector = ReadString(p, "group");
                        if (groupSelector != null)
                            groups = new List<object> { FindGroup(groups, groupSelector) };
                        return Page(groups.SelectMany(group =>
                            {
                                string groupGuid = SerializedString(group, "m_GUID");
                                return Items(Read(group, "entries")).Select(entry => Entry(entry, groupGuid));
                            })
                            .OrderBy(row => (string)row["guid"], StringComparer.Ordinal), p,
                            action + ":" + groupSelector);
                    case "entry_find":
                        string guid = ReadString(p, "guid");
                        string path = ReadString(p, "path");
                        if ((guid == null) == (path == null))
                            throw new ArgumentException("Provide exactly one of 'guid' or 'path'.");
                        if (path != null)
                        {
                            path = path.Replace('\\', '/');
                            if (!(path.StartsWith("Assets/", StringComparison.Ordinal)
                                || path.StartsWith("Packages/", StringComparison.Ordinal))
                                || path.Split('/').Any(part => part == ".." || part == "."))
                                throw new ArgumentException("'path' must be a project asset path without traversal.");
                            guid = AssetDatabase.AssetPathToGUID(path);
                        }
                        else if (guid.Length != 32 || guid.Any(c => !Uri.IsHexDigit(c)))
                            throw new ArgumentException("'guid' must contain exactly 32 hexadecimal characters.");
                        guid = guid.ToLowerInvariant();
                        // Explicit entries only: folders do not imply a separate registration for every child.
                        object found = Invoke(settings, "FindAssetEntry", new[] { typeof(string), typeof(bool) }, guid, false);
                        return new SuccessResponse("Explicit Addressables entry lookup.", new
                        {
                            found = found != null,
                            entry = found == null ? null : Entry(found, Read(found, "parentGroup"))
                        });
                    case "label_list":
                        return Page(Items(Invoke(settings, "GetLabels", Type.EmptyTypes))
                            .Select(label => new JObject { ["name"] = label.ToString() })
                            .OrderBy(row => (string)row["name"], StringComparer.Ordinal), p, action);
                    case "profile_list":
                        object profiles = Read(settings, "profileSettings");
                        return Page(Items(Invoke(profiles, "GetAllProfileNames", Type.EmptyTypes))
                            .Select(name => new JObject
                            {
                                ["name"] = name.ToString(),
                                ["id"] = (string)Invoke(profiles, "GetProfileId", new[] { typeof(string) }, name.ToString())
                            }).OrderBy(row => (string)row["id"], StringComparer.Ordinal), p, action);
                    case "profile_get":
                        object profileSettings = Read(settings, "profileSettings");
                        string profileId = ReadString(p, "profile_id") ?? SerializedString(settings, "m_ActiveProfileId");
                        string profileName = (string)Invoke(profileSettings, "GetProfileName", new[] { typeof(string) }, profileId);
                        if (string.IsNullOrEmpty(profileName))
                            return new ErrorResponse("ADDRESSABLES_PROFILE_NOT_FOUND", new { profile_id = profileId });
                        // Raw profile values preserve tokens; evaluating arbitrary runtime tokens is a separate operation.
                        return Page(Items(Invoke(profileSettings, "GetVariableNames", Type.EmptyTypes))
                            .Select(name => new JObject
                            {
                                ["name"] = name.ToString(),
                                ["value"] = (string)Invoke(profileSettings, "GetValueByName",
                                    new[] { typeof(string), typeof(string) }, profileId, name.ToString())
                            }).OrderBy(row => (string)row["name"], StringComparer.Ordinal), p, action + ":" + profileId);
                    default:
                        return Mutate(settings, groups, action, p);
                }
            }
            catch (ArgumentException ex)
            {
                return new ErrorResponse("INVALID_ARGUMENT", new { message = ex.Message });
            }
            catch (MissingMemberException ex)
            {
                return new ErrorResponse("ADDRESSABLES_API_UNSUPPORTED", new { message = ex.Message });
            }
            catch (TargetInvocationException ex)
            {
                return new ErrorResponse("ADDRESSABLES_QUERY_FAILED", new { message = ex.InnerException?.Message ?? ex.Message });
            }
            catch (Exception ex)
            {
                McpLog.Error($"[ManageAddressables] Query failed: {ex}");
                return new ErrorResponse("ADDRESSABLES_QUERY_FAILED", new { message = ex.Message });
            }
        }

        private static string ReadString(ToolParams p, string name, bool required = false)
        {
            var token = p.GetRaw(name);
            if (token == null || token.Type == JTokenType.Null)
            {
                if (required) throw new ArgumentException($"'{name}' is required.");
                return null;
            }
            if (token.Type != JTokenType.String || string.IsNullOrWhiteSpace((string)token))
                throw new ArgumentException($"'{name}' must be a non-empty string.");
            return (string)token;
        }

        private static object Read(object target, string name)
        {
            var property = target.GetType().GetProperty(name, BindingFlags.Public | BindingFlags.Instance);
            if (property == null) throw new MissingMemberException(target.GetType().FullName, name);
            return property.GetValue(target);
        }

        private static string Text(object target, string name) => (string)Read(target, name);

        private static string SerializedString(object target, string name)
        {
            // Profile IDs and group identifiers have public getters that repair empty values. Queries must not do that.
            using var serialized = new SerializedObject((UnityEngine.Object)target);
            var property = serialized.FindProperty(name);
            if (property == null) throw new MissingMemberException(target.GetType().FullName, name);
            return property.stringValue;
        }

        private static object Invoke(object target, string name, Type[] types, params object[] args)
        {
            var method = target.GetType().GetMethod(name, types);
            if (method == null) throw new MissingMethodException(target.GetType().FullName, name);
            return method.Invoke(target, args);
        }

        private static IEnumerable<object> Items(object value) => ((IEnumerable)value).Cast<object>()
            .Where(item => item != null && (!(item is UnityEngine.Object obj) || obj != null));

        private static object FindGroup(List<object> groups, string selector)
        {
            var byGuid = groups.Where(group => string.Equals(SerializedString(group, "m_GUID"), selector, StringComparison.OrdinalIgnoreCase)).ToList();
            var matches = byGuid.Count > 0 ? byGuid : groups.Where(group => SerializedString(group, "m_GroupName") == selector).ToList();
            if (matches.Count != 1)
                throw new ArgumentException(matches.Count == 0 ? "Group not found." : "Group name is ambiguous; use its GUID.");
            return matches[0];
        }

        private static JObject Entry(object entry, object group) =>
            Entry(entry, group == null ? null : SerializedString(group, "m_GUID"));

        private static JObject Entry(object entry, string groupGuid) => new JObject
        {
            ["guid"] = Text(entry, "guid"),
            ["path"] = Text(entry, "AssetPath"),
            ["address"] = Text(entry, "address"),
            ["labels"] = new JArray(Items(Read(entry, "labels")).Select(label => label.ToString())
                .OrderBy(label => label, StringComparer.Ordinal)),
            ["is_folder"] = AssetDatabase.IsValidFolder(Text(entry, "AssetPath")),
            ["group_guid"] = groupGuid
        };

        private static object Page(IEnumerable<JObject> source, ToolParams p, string scope)
        {
            int size = 50;
            var sizeToken = p.GetRaw("page_size");
            if (sizeToken != null && sizeToken.Type != JTokenType.Null
                && (sizeToken.Type != JTokenType.Integer
                    || !int.TryParse(sizeToken.ToString(), NumberStyles.None, CultureInfo.InvariantCulture, out size)
                    || size < 1 || size > 200))
                throw new ArgumentException("'page_size' must be an integer between 1 and 200.");
            var rows = new JArray(source);
            string snapshot;
            using (var hash = SHA256.Create())
                snapshot = BitConverter.ToString(hash.ComputeHash(Encoding.UTF8.GetBytes(scope + rows.ToString(Formatting.None))))
                    .Replace("-", "").ToLowerInvariant();
            int offset = 0;
            string cursor = ReadString(p, "cursor");
            if (cursor != null)
            {
                string[] parts = cursor.Split(':');
                if (parts.Length != 2 || parts[0] != snapshot
                    || !int.TryParse(parts[1], NumberStyles.None, CultureInfo.InvariantCulture, out offset)
                    || offset < 0 || offset > rows.Count)
                    throw new ArgumentException("Invalid or stale cursor. Restart the query without a cursor.");
            }
            int next = Math.Min(offset + size, rows.Count);
            return new SuccessResponse("Addressables query page.", new
            {
                items = new JArray(rows.Skip(offset).Take(size)),
                total = rows.Count,
                next_cursor = next < rows.Count ? snapshot + ":" + next.ToString(CultureInfo.InvariantCulture) : null
            });
        }
    }
}
