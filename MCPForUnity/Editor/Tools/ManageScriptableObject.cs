using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    /// <summary>
    /// Single tool for ScriptableObject workflows:
    /// - action=create: create a ScriptableObject asset (and optionally apply patches)
    /// - action=modify: apply serialized property patches to an existing asset
    ///
    /// Patching is performed via SerializedObject/SerializedProperty paths (Unity-native), not reflection.
    /// </summary>
    [McpForUnityTool("manage_scriptable_object", AutoRegister = false)]
    public static class ManageScriptableObject
    {
        private const string CodeCompilingOrReloading = "compiling_or_reloading";
        private const string CodeInvalidParams = "invalid_params";
        private const string CodeTypeNotFound = "type_not_found";
        private const string CodeInvalidFolderPath = "invalid_folder_path";
        private const string CodeTargetNotFound = "target_not_found";
        private const string CodeAssetCreateFailed = "asset_create_failed";

        private static readonly HashSet<string> ValidActions = new(StringComparer.OrdinalIgnoreCase)
        {
            // NOTE: Action strings are normalized by NormalizeAction() (lowercased, '_'/'-' removed),
            // so we only need the canonical normalized forms here.
            "create",
            "createso",
            "modify",
            "modifyso",
        };

        public static object HandleCommand(JObject @params)
        {
            if (@params == null)
            {
                return new ErrorResponse(CodeInvalidParams);
            }

            if (EditorApplication.isCompiling || EditorApplication.isUpdating)
            {
                // Unity is transient; treat as retryable on the client side.
                return new ErrorResponse(CodeCompilingOrReloading, new { hint = "retry" });
            }

            // Allow JSON-string parameters for objects/arrays.
            JsonUtil.CoerceJsonStringParameter(@params, "target");
            CoerceJsonStringArrayParameter(@params, "patches");

            string actionRaw = @params["action"]?.ToString();
            if (string.IsNullOrWhiteSpace(actionRaw))
            {
                return new ErrorResponse(CodeInvalidParams, new { message = "'action' is required.", validActions = ValidActions.ToArray() });
            }

            string action = NormalizeAction(actionRaw);
            if (!ValidActions.Contains(action))
            {
                return new ErrorResponse(CodeInvalidParams, new { message = $"Unknown action: '{actionRaw}'.", validActions = ValidActions.ToArray() });
            }

            if (IsCreateAction(action))
            {
                return HandleCreate(@params);
            }

            return HandleModify(@params);
        }

        private static object HandleCreate(JObject @params)
        {
            string typeName = @params["typeName"]?.ToString() ?? @params["type_name"]?.ToString();
            string folderPath = @params["folderPath"]?.ToString() ?? @params["folder_path"]?.ToString();
            string assetName = @params["assetName"]?.ToString() ?? @params["asset_name"]?.ToString();
            bool overwrite = @params["overwrite"]?.ToObject<bool?>() ?? false;

            if (string.IsNullOrWhiteSpace(typeName))
            {
                return new ErrorResponse(CodeInvalidParams, new { message = "'typeName' is required." });
            }

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                return new ErrorResponse(CodeInvalidParams, new { message = "'folderPath' is required." });
            }

            if (string.IsNullOrWhiteSpace(assetName))
            {
                return new ErrorResponse(CodeInvalidParams, new { message = "'assetName' is required." });
            }

            if (assetName.Contains("/") || assetName.Contains("\\"))
            {
                return new ErrorResponse(CodeInvalidParams, new { message = "'assetName' must not contain path separators." });
            }

            if (!TryNormalizeFolderPath(folderPath, out var normalizedFolder, out var folderNormalizeError))
            {
                return new ErrorResponse(CodeInvalidFolderPath, new { message = folderNormalizeError, folderPath });
            }

            if (!EnsureFolderExists(normalizedFolder, out var folderError))
            {
                return new ErrorResponse(CodeInvalidFolderPath, new { message = folderError, folderPath = normalizedFolder });
            }

            var resolvedType = ResolveType(typeName);
            if (resolvedType == null || !typeof(ScriptableObject).IsAssignableFrom(resolvedType))
            {
                return new ErrorResponse(CodeTypeNotFound, new { message = $"ScriptableObject type not found: '{typeName}'", typeName });
            }

            string fileName = assetName.EndsWith(".asset", StringComparison.OrdinalIgnoreCase)
                ? assetName
                : assetName + ".asset";
            string desiredPath = $"{normalizedFolder.TrimEnd('/')}/{fileName}";
            string finalPath = overwrite ? desiredPath : AssetDatabase.GenerateUniqueAssetPath(desiredPath);

            ScriptableObject instance;
            try
            {
                instance = ScriptableObject.CreateInstance(resolvedType);
                if (instance == null)
                {
                    return new ErrorResponse(CodeAssetCreateFailed, new { message = "CreateInstance returned null.", typeName = resolvedType.FullName });
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse(CodeAssetCreateFailed, new { message = ex.Message, typeName = resolvedType.FullName });
            }

            // GUID-preserving overwrite logic
            bool isNewAsset = true;
            try
            {
                if (overwrite)
                {
                    var existingAsset = AssetDatabase.LoadAssetAtPath<ScriptableObject>(finalPath);
                    if (existingAsset != null && existingAsset.GetType() == resolvedType)
                    {
                        // Preserve GUID by overwriting existing asset data in-place
                        EditorUtility.CopySerialized(instance, existingAsset);
                        
                        // Fix for "Main Object Name does not match filename" warning:
                        // CopySerialized overwrites the name with the (empty) name of the new instance.
                        // We must restore the correct name to match the filename.
                        existingAsset.name = Path.GetFileNameWithoutExtension(finalPath);

                        UnityEngine.Object.DestroyImmediate(instance); // Destroy temporary instance
                        instance = existingAsset; // Proceed with patching the existing asset
                        isNewAsset = false;
                        
                        // Mark dirty to ensure changes are picked up
                        EditorUtility.SetDirty(instance);
                    }
                    else if (existingAsset != null)
                    {
                        // Type mismatch or not a ScriptableObject - must delete and recreate to change type, losing GUID
                        // (Or we could warn, but overwrite usually implies replacing)
                        AssetDatabase.DeleteAsset(finalPath);
                    }
                }

                if (isNewAsset)
                {
                    // Ensure the new instance has the correct name before creating asset to avoid warnings
                    instance.name = Path.GetFileNameWithoutExtension(finalPath);
                    AssetDatabase.CreateAsset(instance, finalPath);
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse(CodeAssetCreateFailed, new { message = ex.Message, path = finalPath });
            }

            string guid = AssetDatabase.AssetPathToGUID(finalPath);
            var patchesToken = @params["patches"];
            object patchResults = null;
            var warnings = new List<string>();

            if (patchesToken is JArray patches && patches.Count > 0)
            {
                var patchApply = ApplyPatches(instance, patches);
                patchResults = patchApply.results;
                warnings.AddRange(patchApply.warnings);
            }

            EditorUtility.SetDirty(instance);
            AssetDatabase.SaveAssets();

            return new SuccessResponse(
                "ScriptableObject created.",
                new
                {
                    guid,
                    path = finalPath,
                    typeNameResolved = resolvedType.FullName,
                    patchResults,
                    warnings = warnings.Count > 0 ? warnings : null
                }
            );
        }

        private static object HandleModify(JObject @params)
        {
            if (!TryResolveTarget(@params["target"], out var target, out var targetPath, out var targetGuid, out var err))
            {
                return err;
            }

            var patchesToken = @params["patches"];
            if (patchesToken == null || patchesToken.Type == JTokenType.Null)
            {
                return new ErrorResponse(CodeInvalidParams, new { message = "'patches' is required.", targetPath, targetGuid });
            }

            if (patchesToken is not JArray patches)
            {
                return new ErrorResponse(CodeInvalidParams, new { message = "'patches' must be an array.", targetPath, targetGuid });
            }

            var (results, warnings) = ApplyPatches(target, patches);

            return new SuccessResponse(
                "Serialized properties patched.",
                new
                {
                    targetGuid,
                    targetPath,
                    targetTypeName = target.GetType().FullName,
                    results,
                    warnings = warnings.Count > 0 ? warnings : null
                }
            );
        }

        private static (List<object> results, List<string> warnings) ApplyPatches(UnityEngine.Object target, JArray patches)
        {
            var warnings = new List<string>();
            var results = new List<object>(patches.Count);
            bool anyChanged = false;

            var so = new SerializedObject(target);
            so.Update();

            for (int i = 0; i < patches.Count; i++)
            {
                if (patches[i] is not JObject patchObj)
                {
                    results.Add(new { propertyPath = "", op = "", ok = false, message = $"Patch at index {i} must be an object." });
                    continue;
                }

                string propertyPath = patchObj["propertyPath"]?.ToString()
                    ?? patchObj["property_path"]?.ToString()
                    ?? patchObj["path"]?.ToString();
                string op = (patchObj["op"]?.ToString() ?? "set").Trim();
                if (string.IsNullOrWhiteSpace(propertyPath))
                {
                    results.Add(new { propertyPath = propertyPath ?? "", op, ok = false, message = "Missing required field: propertyPath" });
                    continue;
                }

                if (string.IsNullOrWhiteSpace(op))
                {
                    op = "set";
                }

                var patchResult = ApplyPatch(so, propertyPath, op, patchObj, out bool changed);
                anyChanged |= changed;
                results.Add(patchResult);

                // Array resize should be applied immediately so later paths resolve.
                if (string.Equals(op, "array_resize", StringComparison.OrdinalIgnoreCase) && changed)
                {
                    so.ApplyModifiedProperties();
                    so.Update();
                }
            }

            if (anyChanged)
            {
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(target);
                AssetDatabase.SaveAssets();
            }

            return (results, warnings);
        }

        private static object ApplyPatch(SerializedObject so, string propertyPath, string op, JObject patchObj, out bool changed)
        {
            changed = false;
            try
            {
                string normalizedOp = op.Trim().ToLowerInvariant();

                switch (normalizedOp)
                {
                    case "array_resize":
                        return ApplyArrayResize(so, propertyPath, patchObj, out changed);
                    case "set":
                    default:
                        return ApplySet(so, propertyPath, patchObj, out changed);
                }
            }
            catch (Exception ex)
            {
                return new { propertyPath, op, ok = false, message = ex.Message };
            }
        }

        private static object ApplyArrayResize(SerializedObject so, string propertyPath, JObject patchObj, out bool changed)
        {
            changed = false;
            if (!TryGetInt(patchObj["value"], out int newSize))
            {
                return new { propertyPath, op = "array_resize", ok = false, message = "array_resize requires integer 'value'." };
            }

            newSize = Math.Max(0, newSize);

            // Unity supports resizing either:
            // - the array/list property itself (prop.isArray -> prop.arraySize)
            // - the synthetic leaf property "<array>.Array.size" (prop.intValue)
            //
            // Different Unity versions/serialization edge cases can fail to resolve the synthetic leaf via FindProperty
            // (or can return different property types), so we keep a "best-effort" fallback:
            // - Prefer acting on the requested path if it resolves.
            // - If the requested path doesn't resolve, try to resolve the *array property* and set arraySize directly.
            SerializedProperty prop = so.FindProperty(propertyPath);
            SerializedProperty arrayProp = null;
            if (propertyPath.EndsWith(".Array.size", StringComparison.Ordinal))
            {
                // Caller explicitly targeted the synthetic leaf. Resolve the parent array property as a fallback
                // (Unity sometimes fails to resolve the synthetic leaf in certain serialization contexts).
                var arrayPath = propertyPath.Substring(0, propertyPath.Length - ".Array.size".Length);
                arrayProp = so.FindProperty(arrayPath);
            }
            else
            {
                // Caller targeted either the array property itself (e.g., "items") or some other property.
                // If it's already an array, we can resize it directly. Otherwise, we attempt to resolve
                // a synthetic ".Array.size" leaf as a convenience, which some clients may pass.
                arrayProp = prop != null && prop.isArray ? prop : so.FindProperty(propertyPath + ".Array.size");
            }

            if (prop == null)
            {
                // If we failed to find the direct property but we *can* find the array property, use that.
                if (arrayProp != null && arrayProp.isArray)
                {
                    if (arrayProp.arraySize != newSize)
                    {
                        arrayProp.arraySize = newSize;
                        changed = true;
                    }
                    return new
                    {
                        propertyPath,
                        op = "array_resize",
                        ok = true,
                        resolvedPropertyType = "Array",
                        message = $"Set array size to {newSize}."
                    };
                }

                return new { propertyPath, op = "array_resize", ok = false, message = $"Property not found: {propertyPath}" };
            }

            // Unity may represent ".Array.size" as either Integer or ArraySize depending on version.
            if ((prop.propertyType == SerializedPropertyType.Integer || prop.propertyType == SerializedPropertyType.ArraySize)
                && propertyPath.EndsWith(".Array.size", StringComparison.Ordinal))
            {
                // We successfully resolved the synthetic leaf; write the size through its intValue.
                if (prop.intValue != newSize)
                {
                    prop.intValue = newSize;
                    changed = true;
                }
                return new { propertyPath, op = "array_resize", ok = true, resolvedPropertyType = prop.propertyType.ToString(), message = $"Set array size to {newSize}." };
            }

            if (prop.isArray)
            {
                // We resolved the array property itself; write through arraySize.
                if (prop.arraySize != newSize)
                {
                    prop.arraySize = newSize;
                    changed = true;
                }
                return new { propertyPath, op = "array_resize", ok = true, resolvedPropertyType = "Array", message = $"Set array size to {newSize}." };
            }

            return new { propertyPath, op = "array_resize", ok = false, resolvedPropertyType = prop.propertyType.ToString(), message = $"Property is not an array or array-size field: {propertyPath}" };
        }

        private static object ApplySet(SerializedObject so, string propertyPath, JObject patchObj, out bool changed)
        {
            changed = false;
            var prop = so.FindProperty(propertyPath);
            if (prop == null)
            {
                return new { propertyPath, op = "set", ok = false, message = $"Property not found: {propertyPath}" };
            }

            if (prop.propertyType == SerializedPropertyType.ObjectReference)
            {
                var refObj = patchObj["ref"] as JObject;
                UnityEngine.Object newRef = null;
                string refGuid = refObj?["guid"]?.ToString();
                string refPath = refObj?["path"]?.ToString();

                if (refObj == null && patchObj["value"]?.Type == JTokenType.Null)
                {
                    newRef = null;
                }
                else if (!string.IsNullOrEmpty(refGuid) || !string.IsNullOrEmpty(refPath))
                {
                    string resolvedPath = !string.IsNullOrEmpty(refGuid)
                        ? AssetDatabase.GUIDToAssetPath(refGuid)
                        : AssetPathUtility.SanitizeAssetPath(refPath);

                    if (!string.IsNullOrEmpty(resolvedPath))
                    {
                        newRef = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(resolvedPath);
                    }
                }

                if (prop.objectReferenceValue != newRef)
                {
                    prop.objectReferenceValue = newRef;
                    changed = true;
                }

                return new { propertyPath, op = "set", ok = true, resolvedPropertyType = prop.propertyType.ToString(), message = newRef == null ? "Cleared reference." : "Set reference." };
            }

            var valueToken = patchObj["value"];
            if (valueToken == null)
            {
                return new { propertyPath, op = "set", ok = false, resolvedPropertyType = prop.propertyType.ToString(), message = "Missing required field: value" };
            }

            bool ok = TrySetValue(prop, valueToken, out string message);
            changed = ok;
            return new { propertyPath, op = "set", ok, resolvedPropertyType = prop.propertyType.ToString(), message };
        }

        private static bool TrySetValue(SerializedProperty prop, JToken valueToken, out string message)
        {
            message = null;
            try
            {
                // Supported Types: Integer, Boolean, Float, String, Enum, Vector2, Vector3, Vector4, Color
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        if (!TryGetInt(valueToken, out var intVal)) { message = "Expected integer value."; return false; }
                        prop.intValue = intVal; message = "Set int."; return true;

                    case SerializedPropertyType.Boolean:
                        if (!TryGetBool(valueToken, out var boolVal)) { message = "Expected boolean value."; return false; }
                        prop.boolValue = boolVal; message = "Set bool."; return true;

                    case SerializedPropertyType.Float:
                        if (!TryGetFloat(valueToken, out var floatVal)) { message = "Expected float value."; return false; }
                        prop.floatValue = floatVal; message = "Set float."; return true;

                    case SerializedPropertyType.String:
                        prop.stringValue = valueToken.Type == JTokenType.Null ? null : valueToken.ToString();
                        message = "Set string."; return true;

                    case SerializedPropertyType.Enum:
                        return TrySetEnum(prop, valueToken, out message);

                    case SerializedPropertyType.Vector2:
                        if (!TryGetVector2(valueToken, out var v2)) { message = "Expected Vector2 (array or object)."; return false; }
                        prop.vector2Value = v2; message = "Set Vector2."; return true;

                    case SerializedPropertyType.Vector3:
                        if (!TryGetVector3(valueToken, out var v3)) { message = "Expected Vector3 (array or object)."; return false; }
                        prop.vector3Value = v3; message = "Set Vector3."; return true;

                    case SerializedPropertyType.Vector4:
                        if (!TryGetVector4(valueToken, out var v4)) { message = "Expected Vector4 (array or object)."; return false; }
                        prop.vector4Value = v4; message = "Set Vector4."; return true;

                    case SerializedPropertyType.Color:
                        if (!TryGetColor(valueToken, out var col)) { message = "Expected Color (array or object)."; return false; }
                        prop.colorValue = col; message = "Set Color."; return true;

                    default:
                        message = $"Unsupported SerializedPropertyType: {prop.propertyType}";
                        return false;
                }
            }
            catch (Exception ex)
            {
                message = ex.Message;
                return false;
            }
        }

        private static bool TrySetEnum(SerializedProperty prop, JToken valueToken, out string message)
        {
            message = null;
            var names = prop.enumNames;
            if (names == null || names.Length == 0) { message = "Enum has no names."; return false; }

            if (valueToken.Type == JTokenType.Integer)
            {
                int idx = valueToken.Value<int>();
                if (idx < 0 || idx >= names.Length) { message = $"Enum index out of range: {idx}"; return false; }
                prop.enumValueIndex = idx; message = "Set enum."; return true;
            }

            string s = valueToken.ToString();
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], s, StringComparison.OrdinalIgnoreCase))
                {
                    prop.enumValueIndex = i; message = "Set enum."; return true;
                }
            }
            message = $"Unknown enum name '{s}'.";
            return false;
        }

        private static bool TryResolveTarget(JToken targetToken, out UnityEngine.Object target, out string targetPath, out string targetGuid, out object error)
        {
            target = null;
            targetPath = null;
            targetGuid = null;
            error = null;

            if (targetToken is not JObject targetObj)
            {
                error = new ErrorResponse(CodeInvalidParams, new { message = "'target' must be an object with {guid|path}." });
                return false;
            }

            string guid = targetObj["guid"]?.ToString();
            string path = targetObj["path"]?.ToString();

            if (string.IsNullOrWhiteSpace(guid) && string.IsNullOrWhiteSpace(path))
            {
                error = new ErrorResponse(CodeInvalidParams, new { message = "'target' must include 'guid' or 'path'." });
                return false;
            }

            string resolvedPath = !string.IsNullOrWhiteSpace(guid)
                ? AssetDatabase.GUIDToAssetPath(guid)
                : AssetPathUtility.SanitizeAssetPath(path);

            if (string.IsNullOrWhiteSpace(resolvedPath))
            {
                error = new ErrorResponse(CodeTargetNotFound, new { message = "Could not resolve target path.", guid, path });
                return false;
            }

            var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(resolvedPath);
            if (obj == null)
            {
                error = new ErrorResponse(CodeTargetNotFound, new { message = "Target asset not found.", targetPath = resolvedPath, targetGuid = guid });
                return false;
            }

            target = obj;
            targetPath = resolvedPath;
            targetGuid = string.IsNullOrWhiteSpace(guid) ? AssetDatabase.AssetPathToGUID(resolvedPath) : guid;
            return true;
        }

        private static void CoerceJsonStringArrayParameter(JObject @params, string paramName)
        {
            var token = @params?[paramName];
            if (token != null && token.Type == JTokenType.String)
            {
                try
                {
                    var parsed = JToken.Parse(token.ToString());
                    if (parsed is JArray arr)
                    {
                        @params[paramName] = arr;
                    }
                }
                catch (Exception e)
                {
                    Debug.LogWarning($"[MCP] Could not parse '{paramName}' JSON string: {e.Message}");
                }
            }
        }

        private static bool EnsureFolderExists(string folderPath, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(folderPath))
            {
                error = "Folder path is empty.";
                return false;
            }

            // Expect normalized input here (Assets/... or Assets).
            string sanitized = SanitizeSlashes(folderPath);

            if (!sanitized.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase)
                && !string.Equals(sanitized, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                error = "Folder path must be under Assets/.";
                return false;
            }

            if (string.Equals(sanitized, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            sanitized = sanitized.TrimEnd('/');
            if (AssetDatabase.IsValidFolder(sanitized))
            {
                return true;
            }

            // Create recursively from Assets/
            var parts = sanitized.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0 || !string.Equals(parts[0], "Assets", StringComparison.OrdinalIgnoreCase))
            {
                error = "Folder path must start with Assets/";
                return false;
            }

            string current = "Assets";
            for (int i = 1; i < parts.Length; i++)
            {
                string next = current + "/" + parts[i];
                if (!AssetDatabase.IsValidFolder(next))
                {
                    string guid = AssetDatabase.CreateFolder(current, parts[i]);
                    if (string.IsNullOrEmpty(guid))
                    {
                        error = $"Failed to create folder: {next}";
                        return false;
                    }
                }
                current = next;
            }

            return AssetDatabase.IsValidFolder(sanitized);
        }

        private static string SanitizeSlashes(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return path;
            }

            var s = path.Replace('\\', '/');
            while (s.IndexOf("//", StringComparison.Ordinal) >= 0)
            {
                s = s.Replace("//", "/", StringComparison.Ordinal);
            }
            return s;
        }

        private static bool TryNormalizeFolderPath(string folderPath, out string normalized, out string error)
        {
            normalized = null;
            error = null;

            if (string.IsNullOrWhiteSpace(folderPath))
            {
                error = "Folder path is empty.";
                return false;
            }

            var s = SanitizeSlashes(folderPath.Trim());

            // Reject obvious non-project/invalid roots. We only support Assets/ (and relative paths that will be rooted under Assets/).
            if (s.StartsWith("/", StringComparison.Ordinal) 
                || s.StartsWith("file:", StringComparison.OrdinalIgnoreCase)
                || Regex.IsMatch(s, @"^[a-zA-Z]:"))
            {
                error = "Folder path must be a project-relative path under Assets/.";
                return false;
            }

            if (s.StartsWith("Packages/", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("ProjectSettings/", StringComparison.OrdinalIgnoreCase)
                || s.StartsWith("Library/", StringComparison.OrdinalIgnoreCase))
            {
                error = "Folder path must be under Assets/.";
                return false;
            }

            if (string.Equals(s, "Assets", StringComparison.OrdinalIgnoreCase))
            {
                normalized = "Assets";
                return true;
            }

            if (s.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase))
            {
                normalized = s.TrimEnd('/');
                return true;
            }

            // Allow relative paths like "Temp/MyFolder" and root them under Assets/.
            normalized = ("Assets/" + s.TrimStart('/')).TrimEnd('/');
            return true;
        }

        private static bool TryGetInt(JToken token, out int value)
        {
            value = default;
            if (token == null || token.Type == JTokenType.Null) return false;
            try
            {
                if (token.Type == JTokenType.Integer) { value = token.Value<int>(); return true; }
                if (token.Type == JTokenType.Float) { value = Convert.ToInt32(token.Value<double>()); return true; }
                var s = token.ToString().Trim();
                return int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out value);
            }
            catch { return false; }
        }

        private static bool TryGetFloat(JToken token, out float value)
        {
            value = default;
            if (token == null || token.Type == JTokenType.Null) return false;
            try
            {
                if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer) { value = token.Value<float>(); return true; }
                var s = token.ToString().Trim();
                return float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
            catch { return false; }
        }

        private static bool TryGetBool(JToken token, out bool value)
        {
            value = default;
            if (token == null || token.Type == JTokenType.Null) return false;
            try
            {
                if (token.Type == JTokenType.Boolean) { value = token.Value<bool>(); return true; }
                var s = token.ToString().Trim();
                return bool.TryParse(s, out value);
            }
            catch { return false; }
        }

        // --- Vector/Color Parsing Helpers ---

        private static bool TryGetVector2(JToken token, out Vector2 value)
        {
            value = default;
            if (token == null || token.Type == JTokenType.Null) return false;

            // Handle [x, y]
            if (token is JArray arr && arr.Count >= 2)
            {
                if (TryGetFloat(arr[0], out float x) && TryGetFloat(arr[1], out float y))
                {
                    value = new Vector2(x, y);
                    return true;
                }
            }
            // Handle { "x": ..., "y": ... }
            if (token is JObject obj)
            {
                if (TryGetFloat(obj["x"], out float x) && TryGetFloat(obj["y"], out float y))
                {
                    value = new Vector2(x, y);
                    return true;
                }
            }
            return false;
        }

        private static bool TryGetVector3(JToken token, out Vector3 value)
        {
            value = default;
            if (token == null || token.Type == JTokenType.Null) return false;

            // Handle [x, y, z]
            if (token is JArray arr && arr.Count >= 3)
            {
                if (TryGetFloat(arr[0], out float x) && TryGetFloat(arr[1], out float y) && TryGetFloat(arr[2], out float z))
                {
                    value = new Vector3(x, y, z);
                    return true;
                }
            }
            // Handle { "x": ..., "y": ..., "z": ... }
            if (token is JObject obj)
            {
                if (TryGetFloat(obj["x"], out float x) && TryGetFloat(obj["y"], out float y) && TryGetFloat(obj["z"], out float z))
                {
                    value = new Vector3(x, y, z);
                    return true;
                }
            }
            return false;
        }

        private static bool TryGetVector4(JToken token, out Vector4 value)
        {
            value = default;
            if (token == null || token.Type == JTokenType.Null) return false;

            // Handle [x, y, z, w]
            if (token is JArray arr && arr.Count >= 4)
            {
                if (TryGetFloat(arr[0], out float x) && TryGetFloat(arr[1], out float y) 
                    && TryGetFloat(arr[2], out float z) && TryGetFloat(arr[3], out float w))
                {
                    value = new Vector4(x, y, z, w);
                    return true;
                }
            }
            // Handle { "x": ..., "y": ..., "z": ..., "w": ... }
            if (token is JObject obj)
            {
                if (TryGetFloat(obj["x"], out float x) && TryGetFloat(obj["y"], out float y) 
                    && TryGetFloat(obj["z"], out float z) && TryGetFloat(obj["w"], out float w))
                {
                    value = new Vector4(x, y, z, w);
                    return true;
                }
            }
            return false;
        }

        private static bool TryGetColor(JToken token, out Color value)
        {
            value = default;
            if (token == null || token.Type == JTokenType.Null) return false;

            // Handle [r, g, b, a]
            if (token is JArray arr && arr.Count >= 3)
            {
                float r = 0, g = 0, b = 0, a = 1;
                bool ok = TryGetFloat(arr[0], out r) && TryGetFloat(arr[1], out g) && TryGetFloat(arr[2], out b);
                if (arr.Count > 3) TryGetFloat(arr[3], out a);
                if (ok)
                {
                    value = new Color(r, g, b, a);
                    return true;
                }
            }
            // Handle { "r": ..., "g": ..., "b": ..., "a": ... }
            if (token is JObject obj)
            {
                if (TryGetFloat(obj["r"], out float r) && TryGetFloat(obj["g"], out float g) && TryGetFloat(obj["b"], out float b))
                {
                    // Alpha is optional, defaults to 1.0
                    float a = 1.0f;
                    TryGetFloat(obj["a"], out a); 
                    value = new Color(r, g, b, a);
                    return true;
                }
            }
            return false;
        }

        private static string NormalizeAction(string raw)
        {
            var s = raw.Trim();
            s = s.Replace("-", "").Replace("_", "");
            return s.ToLowerInvariant();
        }

        private static bool IsCreateAction(string normalized)
        {
            return normalized == "create" || normalized == "createso";
        }

        private static Type ResolveType(string typeName)
        {
            if (string.IsNullOrWhiteSpace(typeName)) return null;

            var type = Type.GetType(typeName, throwOnError: false);
            if (type != null) return type;

            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => a != null && !a.IsDynamic))
            {
                try
                {
                    type = asm.GetType(typeName, throwOnError: false);
                    if (type != null) return type;
                }
                catch
                {
                    // ignore
                }
            }

            // fallback: scan types by FullName match (covers cases where GetType lookup fails)
            foreach (var asm in AppDomain.CurrentDomain.GetAssemblies().Where(a => a != null && !a.IsDynamic))
            {
                Type[] types;
                try { types = asm.GetTypes(); }
                catch (ReflectionTypeLoadException e) { types = e.Types.Where(t => t != null).ToArray(); }
                catch { continue; }

                foreach (var t in types)
                {
                    if (t == null) continue;
                    if (string.Equals(t.FullName, typeName, StringComparison.Ordinal))
                    {
                        return t;
                    }
                }
            }

            return null;
        }
    }
}
