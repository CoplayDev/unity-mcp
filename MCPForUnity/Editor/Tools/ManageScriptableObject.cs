using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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

            // Phase 5: Dry-run mode - validate patches without applying
            bool dryRun = @params["dryRun"]?.ToObject<bool?>() ?? @params["dry_run"]?.ToObject<bool?>() ?? false;
            
            if (dryRun)
            {
                var validationResults = ValidatePatches(target, patches);
                return new SuccessResponse(
                    "Dry-run validation complete.",
                    new
                    {
                        targetGuid,
                        targetPath,
                        targetTypeName = target.GetType().FullName,
                        dryRun = true,
                        valid = validationResults.All(r => (bool)r.GetType().GetProperty("ok")?.GetValue(r)),
                        validationResults
                    }
                );
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

        /// <summary>
        /// Validates patches without applying them (for dry-run mode).
        /// Checks that property paths exist and that value types are compatible.
        /// </summary>
        private static List<object> ValidatePatches(UnityEngine.Object target, JArray patches)
        {
            var results = new List<object>(patches.Count);
            var so = new SerializedObject(target);
            so.Update();

            for (int i = 0; i < patches.Count; i++)
            {
                if (patches[i] is not JObject patchObj)
                {
                    results.Add(new { index = i, propertyPath = "", op = "", ok = false, message = $"Patch at index {i} must be an object." });
                    continue;
                }

                string propertyPath = patchObj["propertyPath"]?.ToString()
                    ?? patchObj["property_path"]?.ToString()
                    ?? patchObj["path"]?.ToString();
                string op = (patchObj["op"]?.ToString() ?? "set").Trim();

                if (string.IsNullOrWhiteSpace(propertyPath))
                {
                    results.Add(new { index = i, propertyPath = propertyPath ?? "", op, ok = false, message = "Missing required field: propertyPath" });
                    continue;
                }

                // Normalize the path
                string normalizedPath = NormalizePropertyPath(propertyPath);
                string normalizedOp = op.ToLowerInvariant();

                // For array_resize, check if the array exists
                if (normalizedOp == "array_resize")
                {
                    var valueToken = patchObj["value"];
                    if (valueToken == null || valueToken.Type == JTokenType.Null)
                    {
                        results.Add(new { index = i, propertyPath = normalizedPath, op, ok = false, message = "array_resize requires integer 'value'." });
                        continue;
                    }

                    int size = ParamCoercion.CoerceInt(valueToken, -1);
                    if (size < 0)
                    {
                        results.Add(new { index = i, propertyPath = normalizedPath, op, ok = false, message = "array_resize requires non-negative integer 'value'." });
                        continue;
                    }

                    // Check if the array path exists
                    string arrayPath = normalizedPath;
                    if (arrayPath.EndsWith(".Array.size", StringComparison.Ordinal))
                    {
                        arrayPath = arrayPath.Substring(0, arrayPath.Length - ".Array.size".Length);
                    }

                    var arrayProp = so.FindProperty(arrayPath);
                    if (arrayProp == null)
                    {
                        results.Add(new { index = i, propertyPath = normalizedPath, op, ok = false, message = $"Array not found: {arrayPath}" });
                        continue;
                    }

                    if (!arrayProp.isArray)
                    {
                        results.Add(new { index = i, propertyPath = normalizedPath, op, ok = false, message = $"Property is not an array: {arrayPath}" });
                        continue;
                    }

                    results.Add(new { index = i, propertyPath = normalizedPath, op, ok = true, message = $"Will resize to {size}.", currentSize = arrayProp.arraySize });
                    continue;
                }

                // For set operations, check if the property exists (or can be auto-grown)
                var prop = so.FindProperty(normalizedPath);
                
                // Check if it's an auto-growable array element path
                bool isAutoGrowable = false;
                if (prop == null)
                {
                    var match = Regex.Match(normalizedPath, @"^(.+?)\.Array\.data\[(\d+)\]");
                    if (match.Success)
                    {
                        string arrayPath = match.Groups[1].Value;
                        var arrayProp = so.FindProperty(arrayPath);
                        if (arrayProp != null && arrayProp.isArray)
                        {
                            isAutoGrowable = true;
                            // Get the element type info from existing elements or report as growable
                            int targetIndex = int.Parse(match.Groups[2].Value);
                            if (arrayProp.arraySize > 0)
                            {
                                var sampleElement = arrayProp.GetArrayElementAtIndex(0);
                                results.Add(new { 
                                    index = i, 
                                    propertyPath = normalizedPath, 
                                    op, 
                                    ok = true, 
                                    message = $"Will auto-grow array from {arrayProp.arraySize} to {targetIndex + 1}.",
                                    elementType = sampleElement?.propertyType.ToString() ?? "unknown"
                                });
                            }
                            else
                            {
                                results.Add(new { 
                                    index = i, 
                                    propertyPath = normalizedPath, 
                                    op, 
                                    ok = true, 
                                    message = $"Will auto-grow empty array to size {targetIndex + 1}."
                                });
                            }
                            continue;
                        }
                    }
                }

                if (prop == null && !isAutoGrowable)
                {
                    results.Add(new { index = i, propertyPath = normalizedPath, op, ok = false, message = $"Property not found: {normalizedPath}" });
                    continue;
                }

                if (prop != null)
                {
                    // Property exists - validate value format for supported complex types
                    var valueToken = patchObj["value"];
                    string valueValidationMsg = null;
                    bool valueFormatOk = true;
                    
                    // Enhanced dry-run: validate value format for AnimationCurve and Quaternion
                    // Uses shared validators from VectorParsing
                    if (valueToken != null && valueToken.Type != JTokenType.Null)
                    {
                        switch (prop.propertyType)
                        {
                            case SerializedPropertyType.AnimationCurve:
                                valueFormatOk = VectorParsing.ValidateAnimationCurveFormat(valueToken, out valueValidationMsg);
                                break;
                            case SerializedPropertyType.Quaternion:
                                valueFormatOk = VectorParsing.ValidateQuaternionFormat(valueToken, out valueValidationMsg);
                                break;
                        }
                    }
                    
                    if (valueFormatOk)
                    {
                        results.Add(new { 
                            index = i, 
                            propertyPath = normalizedPath, 
                            op, 
                            ok = true, 
                            message = valueValidationMsg ?? "Property found.",
                            propertyType = prop.propertyType.ToString(),
                            isArray = prop.isArray
                        });
                    }
                    else
                    {
                        results.Add(new { 
                            index = i, 
                            propertyPath = normalizedPath, 
                            op, 
                            ok = false, 
                            message = valueValidationMsg,
                            propertyType = prop.propertyType.ToString(),
                            isArray = prop.isArray
                        });
                    }
                }
            }

            return results;
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
                // Phase 1.1: Normalize friendly path syntax (e.g., myList[5] → myList.Array.data[5])
                string normalizedPath = NormalizePropertyPath(propertyPath);
                string normalizedOp = op.Trim().ToLowerInvariant();

                switch (normalizedOp)
                {
                    case "array_resize":
                        return ApplyArrayResize(so, normalizedPath, patchObj, out changed);
                    case "set":
                    default:
                        return ApplySet(so, normalizedPath, patchObj, out changed);
                }
            }
            catch (Exception ex)
            {
                return new { propertyPath, op, ok = false, message = ex.Message };
            }
        }

        /// <summary>
        /// Normalizes friendly property path syntax to Unity's internal format.
        /// Converts bracket notation (e.g., myList[5]) to Unity's Array.data format (myList.Array.data[5]).
        /// </summary>
        private static string NormalizePropertyPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return path;

            // Pattern: word[number] where it's not already in .Array.data[number] format
            // We need to handle cases like: myList[5], nested.list[0].field, etc.
            // But NOT: myList.Array.data[5] (already in Unity format)
            
            // Replace fieldName[index] with fieldName.Array.data[index]
            // But only if it's not already in Array.data format
            return Regex.Replace(path, @"(\w+)\[(\d+)\]", m =>
            {
                string fieldName = m.Groups[1].Value;
                string index = m.Groups[2].Value;
                
                // Check if this match is already part of .Array.data[index] pattern
                // by checking if the text immediately before the field name is ".Array."
                // and the field name is "data"
                int matchStart = m.Index;
                if (fieldName == "data" && matchStart >= 7) // Length of ".Array."
                {
                    string preceding = path.Substring(matchStart - 7, 7);
                    if (preceding == ".Array.")
                    {
                        // Already in Unity format (e.g., myList.Array.data[0]), return as-is
                        return m.Value;
                    }
                }
                
                return $"{fieldName}.Array.data[{index}]";
            });
        }

        /// <summary>
        /// Ensures an array has sufficient capacity for the given index.
        /// Automatically resizes the array if the target index is beyond current bounds.
        /// </summary>
        /// <param name="so">The SerializedObject containing the array</param>
        /// <param name="path">The normalized property path (must be in Array.data format)</param>
        /// <param name="resized">True if the array was resized</param>
        /// <returns>True if the path is valid for setting, false if it cannot be resolved</returns>
        private static bool EnsureArrayCapacity(SerializedObject so, string path, out bool resized)
        {
            resized = false;
            
            // Match pattern: something.Array.data[N]
            var match = Regex.Match(path, @"^(.+?)\.Array\.data\[(\d+)\]");
            if (!match.Success)
            {
                // Not an array element path, nothing to do
                return true;
            }

            string arrayPath = match.Groups[1].Value;
            if (!int.TryParse(match.Groups[2].Value, out int targetIndex))
            {
                return false;
            }

            var arrayProp = so.FindProperty(arrayPath);
            if (arrayProp == null || !arrayProp.isArray)
            {
                // Array property not found or not an array
                return false;
            }

            if (arrayProp.arraySize <= targetIndex)
            {
                // Need to grow the array
                arrayProp.arraySize = targetIndex + 1;
                so.ApplyModifiedProperties();
                so.Update();
                resized = true;
            }

            return true;
        }

        private static object ApplyArrayResize(SerializedObject so, string propertyPath, JObject patchObj, out bool changed)
        {
            changed = false;
            
            // Use ParamCoercion for robust int parsing
            var valueToken = patchObj["value"];
            if (valueToken == null || valueToken.Type == JTokenType.Null)
            {
                return new { propertyPath, op = "array_resize", ok = false, message = "array_resize requires integer 'value'." };
            }
            
            int newSize = ParamCoercion.CoerceInt(valueToken, -1);
            if (newSize < 0)
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
            
            // Phase 1.2: Auto-resize arrays if targeting an index beyond current bounds
            if (!EnsureArrayCapacity(so, propertyPath, out bool arrayResized))
            {
                // Could not resolve the array path - try to find the property anyway for a better error message
                var checkProp = so.FindProperty(propertyPath);
                if (checkProp == null)
                {
                    // Try to provide helpful context about what went wrong
                    var arrayMatch = Regex.Match(propertyPath, @"^(.+?)\.Array\.data\[(\d+)\]");
                    if (arrayMatch.Success)
                    {
                        string arrayPath = arrayMatch.Groups[1].Value;
                        var arrayProp = so.FindProperty(arrayPath);
                        if (arrayProp == null)
                        {
                            return new { propertyPath, op = "set", ok = false, message = $"Array property not found: {arrayPath}" };
                        }
                        if (!arrayProp.isArray)
                        {
                            return new { propertyPath, op = "set", ok = false, message = $"Property is not an array: {arrayPath}" };
                        }
                    }
                    return new { propertyPath, op = "set", ok = false, message = $"Property not found: {propertyPath}" };
                }
            }
            
            var prop = so.FindProperty(propertyPath);
            if (prop == null)
            {
                return new { propertyPath, op = "set", ok = false, message = $"Property not found: {propertyPath}" };
            }
            
            // Track if we resized - this counts as a change
            if (arrayResized)
            {
                changed = true;
            }

            if (prop.propertyType == SerializedPropertyType.ObjectReference)
            {
                var refObj = patchObj["ref"] as JObject;
                var objRefValue = patchObj["value"];
                UnityEngine.Object newRef = null;
                string refGuid = refObj?["guid"]?.ToString();
                string refPath = refObj?["path"]?.ToString();
                string resolveMethod = "explicit";

                if (refObj == null && objRefValue?.Type == JTokenType.Null)
                {
                    // Explicit null - clear the reference
                    newRef = null;
                    resolveMethod = "cleared";
                }
                else if (!string.IsNullOrEmpty(refGuid) || !string.IsNullOrEmpty(refPath))
                {
                    // Traditional ref object with guid or path
                    string resolvedPath = !string.IsNullOrEmpty(refGuid)
                        ? AssetDatabase.GUIDToAssetPath(refGuid)
                        : AssetPathUtility.SanitizeAssetPath(refPath);

                    if (!string.IsNullOrEmpty(resolvedPath))
                    {
                        newRef = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(resolvedPath);
                    }
                    resolveMethod = !string.IsNullOrEmpty(refGuid) ? "ref.guid" : "ref.path";
                }
                else if (objRefValue?.Type == JTokenType.String)
                {
                    // Phase 4: GUID shorthand - allow plain string value
                    string strVal = objRefValue.ToString();
                    
                    // Check if it's a GUID (32 hex characters, no dashes)
                    if (Regex.IsMatch(strVal, @"^[0-9a-fA-F]{32}$"))
                    {
                        string guidPath = AssetDatabase.GUIDToAssetPath(strVal);
                        if (!string.IsNullOrEmpty(guidPath))
                        {
                            newRef = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(guidPath);
                            resolveMethod = "guid-shorthand";
                        }
                    }
                    // Check if it looks like an asset path
                    else if (strVal.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || 
                             strVal.Contains("/"))
                    {
                        string sanitizedPath = AssetPathUtility.SanitizeAssetPath(strVal);
                        newRef = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sanitizedPath);
                        resolveMethod = "path-shorthand";
                    }
                }

                if (prop.objectReferenceValue != newRef)
                {
                    prop.objectReferenceValue = newRef;
                    changed = true;
                }

                string refMessage = newRef == null ? "Cleared reference." : $"Set reference ({resolveMethod}).";
                return new { propertyPath, op = "set", ok = true, resolvedPropertyType = prop.propertyType.ToString(), message = refMessage };
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
            return TrySetValueRecursive(prop, valueToken, out message, 0);
        }

        /// <summary>
        /// Recursively sets values on SerializedProperties, supporting bulk array and object mapping.
        /// </summary>
        /// <param name="prop">The property to set</param>
        /// <param name="valueToken">The JSON value</param>
        /// <param name="message">Output message describing the result</param>
        /// <param name="depth">Current recursion depth (for safety limits)</param>
        private static bool TrySetValueRecursive(SerializedProperty prop, JToken valueToken, out string message, int depth)
        {
            message = null;
            const int MaxRecursionDepth = 20;

            if (depth > MaxRecursionDepth)
            {
                message = $"Maximum recursion depth ({MaxRecursionDepth}) exceeded. Check for circular references.";
                return false;
            }

            try
            {
                // Phase 3.1: Handle bulk array mapping - JArray value for array/list properties
                if (prop.isArray && prop.propertyType != SerializedPropertyType.String && valueToken is JArray jArray)
                {
                    // Resize the array to match the JSON array
                    prop.arraySize = jArray.Count;
                    
                    // Get the SerializedObject and apply so we can access elements
                    var so = prop.serializedObject;
                    so.ApplyModifiedProperties();
                    so.Update();

                    int successCount = 0;
                    var errors = new List<string>();

                    for (int i = 0; i < jArray.Count; i++)
                    {
                        var elementProp = prop.GetArrayElementAtIndex(i);
                        if (elementProp == null)
                        {
                            errors.Add($"Could not get element at index {i}");
                            continue;
                        }

                        if (TrySetValueRecursive(elementProp, jArray[i], out string elemMessage, depth + 1))
                        {
                            successCount++;
                        }
                        else
                        {
                            errors.Add($"[{i}]: {elemMessage}");
                        }
                    }

                    so.ApplyModifiedProperties();

                    if (errors.Count > 0)
                    {
                        message = $"Set {successCount}/{jArray.Count} elements. Errors: {string.Join("; ", errors)}";
                        return successCount > 0; // Partial success
                    }

                    message = $"Set array with {jArray.Count} elements.";
                    return true;
                }

                // Phase 3.2: Handle bulk object mapping - JObject value for Generic (struct/class) properties
                if (prop.propertyType == SerializedPropertyType.Generic && !prop.isArray && valueToken is JObject jObj)
                {
                    int successCount = 0;
                    var errors = new List<string>();
                    var so = prop.serializedObject;

                    foreach (var kvp in jObj)
                    {
                        string childPath = prop.propertyPath + "." + kvp.Key;
                        var childProp = so.FindProperty(childPath);

                        if (childProp == null)
                        {
                            errors.Add($"Property not found: {kvp.Key}");
                            continue;
                        }

                        if (TrySetValueRecursive(childProp, kvp.Value, out string childMessage, depth + 1))
                        {
                            successCount++;
                        }
                        else
                        {
                            errors.Add($"{kvp.Key}: {childMessage}");
                        }
                    }

                    so.ApplyModifiedProperties();

                    if (errors.Count > 0)
                    {
                        message = $"Set {successCount}/{jObj.Count} fields. Errors: {string.Join("; ", errors)}";
                        return successCount > 0; // Partial success
                    }

                    message = $"Set struct/class with {jObj.Count} fields.";
                    return true;
                }

                // Supported Types: Integer, Boolean, Float, String, Enum, Vector2, Vector3, Vector4, Color
                // Using shared helpers from ParamCoercion and VectorParsing
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        // Use ParamCoercion for robust int parsing
                        int intVal = ParamCoercion.CoerceInt(valueToken, int.MinValue);
                        if (intVal == int.MinValue && valueToken?.Type != JTokenType.Integer)
                        {
                            // Double-check: if it's actually int.MinValue or failed to parse
                            if (valueToken == null || valueToken.Type == JTokenType.Null ||
                                (valueToken.Type == JTokenType.String && !int.TryParse(valueToken.ToString(), out _)))
                            {
                                message = "Expected integer value.";
                                return false;
                            }
                        }
                        prop.intValue = intVal;
                        message = "Set int.";
                        return true;

                    case SerializedPropertyType.Boolean:
                        // Use ParamCoercion for robust bool parsing (handles "true", "1", "yes", etc.)
                        if (valueToken == null || valueToken.Type == JTokenType.Null)
                        {
                            message = "Expected boolean value.";
                            return false;
                        }
                        bool boolVal = ParamCoercion.CoerceBool(valueToken, false);
                        // Verify it actually looked like a bool
                        if (valueToken.Type != JTokenType.Boolean)
                        {
                            string strVal = valueToken.ToString().Trim().ToLowerInvariant();
                            if (strVal != "true" && strVal != "false" && strVal != "1" && strVal != "0" &&
                                strVal != "yes" && strVal != "no" && strVal != "on" && strVal != "off")
                            {
                                message = "Expected boolean value.";
                                return false;
                            }
                        }
                        prop.boolValue = boolVal;
                        message = "Set bool.";
                        return true;

                    case SerializedPropertyType.Float:
                        // Use ParamCoercion for robust float parsing
                        float floatVal = ParamCoercion.CoerceFloat(valueToken, float.NaN);
                        if (float.IsNaN(floatVal))
                        {
                            message = "Expected float value.";
                            return false;
                        }
                        prop.floatValue = floatVal;
                        message = "Set float.";
                        return true;

                    case SerializedPropertyType.String:
                        prop.stringValue = valueToken.Type == JTokenType.Null ? null : valueToken.ToString();
                        message = "Set string.";
                        return true;

                    case SerializedPropertyType.Enum:
                        return TrySetEnum(prop, valueToken, out message);

                    case SerializedPropertyType.Vector2:
                        // Use VectorParsing for Vector2
                        var v2 = VectorParsing.ParseVector2(valueToken);
                        if (v2 == null)
                        {
                            message = "Expected Vector2 (array or object).";
                            return false;
                        }
                        prop.vector2Value = v2.Value;
                        message = "Set Vector2.";
                        return true;

                    case SerializedPropertyType.Vector3:
                        // Use VectorParsing for Vector3
                        var v3 = VectorParsing.ParseVector3(valueToken);
                        if (v3 == null)
                        {
                            message = "Expected Vector3 (array or object).";
                            return false;
                        }
                        prop.vector3Value = v3.Value;
                        message = "Set Vector3.";
                        return true;

                    case SerializedPropertyType.Vector4:
                        // Use VectorParsing for Vector4
                        var v4 = VectorParsing.ParseVector4(valueToken);
                        if (v4 == null)
                        {
                            message = "Expected Vector4 (array or object).";
                            return false;
                        }
                        prop.vector4Value = v4.Value;
                        message = "Set Vector4.";
                        return true;

                    case SerializedPropertyType.Color:
                        // Use VectorParsing for Color
                        var col = VectorParsing.ParseColor(valueToken);
                        if (col == null)
                        {
                            message = "Expected Color (array or object).";
                            return false;
                        }
                        prop.colorValue = col.Value;
                        message = "Set Color.";
                        return true;

                    case SerializedPropertyType.AnimationCurve:
                        return TrySetAnimationCurve(prop, valueToken, out message);

                    case SerializedPropertyType.Quaternion:
                        return TrySetQuaternion(prop, valueToken, out message);

                    case SerializedPropertyType.Generic:
                        // Generic properties (structs/classes) should be handled above with JObject mapping
                        // If we get here, the value wasn't a JObject
                        if (prop.isArray)
                        {
                            message = $"Expected array (JArray) for array property, got {valueToken?.Type.ToString() ?? "null"}.";
                        }
                        else
                        {
                            message = $"Expected object (JObject) for struct/class property, got {valueToken?.Type.ToString() ?? "null"}.";
                        }
                        return false;

                    default:
                        message = $"Unsupported SerializedPropertyType: {prop.propertyType}. " +
                                  "This type cannot be set via MCP patches. Consider editing the .asset file directly " +
                                  "or using Unity's Inspector. For complex types, check if there's a supported alternative format.";
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

        /// <summary>
        /// Sets an AnimationCurve property from a JSON structure.
        /// 
        /// <para><b>Supported formats:</b></para>
        /// <list type="bullet">
        ///   <item>Wrapped: <c>{ "keys": [ { "time": 0, "value": 1.0 }, ... ] }</c></item>
        ///   <item>Direct array: <c>[ { "time": 0, "value": 1.0 }, ... ]</c></item>
        ///   <item>Null/empty: Sets an empty AnimationCurve</item>
        /// </list>
        /// 
        /// <para><b>Keyframe fields:</b></para>
        /// <list type="bullet">
        ///   <item><c>time</c> (float): Keyframe time position. <b>Default: 0</b></item>
        ///   <item><c>value</c> (float): Keyframe value. <b>Default: 0</b></item>
        ///   <item><c>inSlope</c> or <c>inTangent</c> (float): Incoming tangent slope. <b>Default: 0</b></item>
        ///   <item><c>outSlope</c> or <c>outTangent</c> (float): Outgoing tangent slope. <b>Default: 0</b></item>
        ///   <item><c>weightedMode</c> (int): Weighted mode enum (0=None, 1=In, 2=Out, 3=Both). <b>Default: 0 (None)</b></item>
        ///   <item><c>inWeight</c> (float): Incoming tangent weight. <b>Default: 0</b></item>
        ///   <item><c>outWeight</c> (float): Outgoing tangent weight. <b>Default: 0</b></item>
        /// </list>
        /// 
        /// <para><b>Note:</b> All keyframe fields are optional. Missing fields gracefully default to 0,
        /// which produces linear interpolation when both tangents are 0.</para>
        /// </summary>
        /// <param name="prop">The SerializedProperty of type AnimationCurve to set</param>
        /// <param name="valueToken">JSON token containing the curve data</param>
        /// <param name="message">Output message describing the result</param>
        /// <returns>True if successful, false if the format is invalid</returns>
        private static bool TrySetAnimationCurve(SerializedProperty prop, JToken valueToken, out string message)
        {
            message = null;

            if (valueToken == null || valueToken.Type == JTokenType.Null)
            {
                // Set to empty curve
                prop.animationCurveValue = new AnimationCurve();
                message = "Set AnimationCurve to empty.";
                return true;
            }

            JArray keysArray = null;

            // Accept either { "keys": [...] } or just [...]
            if (valueToken is JObject curveObj)
            {
                keysArray = curveObj["keys"] as JArray;
                if (keysArray == null)
                {
                    message = "AnimationCurve object requires 'keys' array. Expected: { \"keys\": [ { \"time\": 0, \"value\": 0 }, ... ] }";
                    return false;
                }
            }
            else if (valueToken is JArray directArray)
            {
                keysArray = directArray;
            }
            else
            {
                message = "AnimationCurve requires object with 'keys' or array of keyframes. " +
                          "Expected: { \"keys\": [ { \"time\": 0, \"value\": 0, \"inSlope\": 0, \"outSlope\": 0 }, ... ] }";
                return false;
            }

            try
            {
                var curve = new AnimationCurve();
                foreach (var keyToken in keysArray)
                {
                    if (keyToken is not JObject keyObj)
                    {
                        message = "Each keyframe must be an object with 'time' and 'value'.";
                        return false;
                    }

                    float time = keyObj["time"]?.Value<float>() ?? 0f;
                    float value = keyObj["value"]?.Value<float>() ?? 0f;
                    float inSlope = keyObj["inSlope"]?.Value<float>() ?? keyObj["inTangent"]?.Value<float>() ?? 0f;
                    float outSlope = keyObj["outSlope"]?.Value<float>() ?? keyObj["outTangent"]?.Value<float>() ?? 0f;

                    var keyframe = new Keyframe(time, value, inSlope, outSlope);

                    // Optional: weighted tangent mode (Unity 2018.1+)
                    if (keyObj["weightedMode"] != null)
                    {
                        int weightedMode = keyObj["weightedMode"].Value<int>();
                        keyframe.weightedMode = (WeightedMode)weightedMode;
                    }
                    if (keyObj["inWeight"] != null)
                    {
                        keyframe.inWeight = keyObj["inWeight"].Value<float>();
                    }
                    if (keyObj["outWeight"] != null)
                    {
                        keyframe.outWeight = keyObj["outWeight"].Value<float>();
                    }

                    curve.AddKey(keyframe);
                }

                prop.animationCurveValue = curve;
                message = $"Set AnimationCurve with {keysArray.Count} keyframes.";
                return true;
            }
            catch (Exception ex)
            {
                message = $"Failed to parse AnimationCurve: {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Sets a Quaternion property from JSON.
        /// 
        /// <para><b>Supported formats:</b></para>
        /// <list type="bullet">
        ///   <item>Euler array: <c>[x, y, z]</c> - Euler angles in degrees</item>
        ///   <item>Raw quaternion array: <c>[x, y, z, w]</c> - Direct quaternion components</item>
        ///   <item>Object format: <c>{ "x": 0, "y": 0, "z": 0, "w": 1 }</c> - Direct components</item>
        ///   <item>Explicit euler: <c>{ "euler": [x, y, z] }</c> - Euler angles in degrees</item>
        ///   <item>Null/empty: Sets Quaternion.identity (no rotation)</item>
        /// </list>
        /// 
        /// <para><b>Format detection:</b></para>
        /// <list type="bullet">
        ///   <item>3-element array → Interpreted as Euler angles (degrees)</item>
        ///   <item>4-element array → Interpreted as raw quaternion [x, y, z, w]</item>
        ///   <item>Object with euler → Uses euler array for rotation</item>
        ///   <item>Object with x, y, z, w → Uses raw quaternion components</item>
        /// </list>
        /// </summary>
        /// <param name="prop">The SerializedProperty of type Quaternion to set</param>
        /// <param name="valueToken">JSON token containing the quaternion data</param>
        /// <param name="message">Output message describing the result</param>
        /// <returns>True if successful, false if the format is invalid</returns>
        private static bool TrySetQuaternion(SerializedProperty prop, JToken valueToken, out string message)
        {
            message = null;

            if (valueToken == null || valueToken.Type == JTokenType.Null)
            {
                prop.quaternionValue = Quaternion.identity;
                message = "Set Quaternion to identity.";
                return true;
            }

            try
            {
                if (valueToken is JArray arr)
                {
                    if (arr.Count == 3)
                    {
                        // Euler angles [x, y, z]
                        var euler = new Vector3(
                            arr[0].Value<float>(),
                            arr[1].Value<float>(),
                            arr[2].Value<float>()
                        );
                        prop.quaternionValue = Quaternion.Euler(euler);
                        message = $"Set Quaternion from Euler({euler.x}, {euler.y}, {euler.z}).";
                        return true;
                    }
                    else if (arr.Count == 4)
                    {
                        // Raw quaternion [x, y, z, w]
                        prop.quaternionValue = new Quaternion(
                            arr[0].Value<float>(),
                            arr[1].Value<float>(),
                            arr[2].Value<float>(),
                            arr[3].Value<float>()
                        );
                        message = "Set Quaternion from [x, y, z, w].";
                        return true;
                    }
                    else
                    {
                        message = "Quaternion array must have 3 elements (Euler) or 4 elements (x, y, z, w).";
                        return false;
                    }
                }
                else if (valueToken is JObject obj)
                {
                    // Check for explicit euler property
                    if (obj["euler"] is JArray eulerArr && eulerArr.Count == 3)
                    {
                        var euler = new Vector3(
                            eulerArr[0].Value<float>(),
                            eulerArr[1].Value<float>(),
                            eulerArr[2].Value<float>()
                        );
                        prop.quaternionValue = Quaternion.Euler(euler);
                        message = $"Set Quaternion from euler: ({euler.x}, {euler.y}, {euler.z}).";
                        return true;
                    }

                    // Object format { x, y, z, w }
                    if (obj["x"] != null && obj["y"] != null && obj["z"] != null && obj["w"] != null)
                    {
                        prop.quaternionValue = new Quaternion(
                            obj["x"].Value<float>(),
                            obj["y"].Value<float>(),
                            obj["z"].Value<float>(),
                            obj["w"].Value<float>()
                        );
                        message = "Set Quaternion from { x, y, z, w }.";
                        return true;
                    }

                    message = "Quaternion object must have { x, y, z, w } or { euler: [x, y, z] }.";
                    return false;
                }
                else
                {
                    message = "Quaternion requires array [x,y,z] (Euler), [x,y,z,w] (raw), or object { x, y, z, w }.";
                    return false;
                }
            }
            catch (Exception ex)
            {
                message = $"Failed to parse Quaternion: {ex.Message}";
                return false;
            }
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
                    McpLog.Warn($"[MCP] Could not parse '{paramName}' JSON string: {e.Message}");
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

            var s = AssetPathUtility.NormalizeSeparators(path);
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

        // NOTE: Local TryGet* helpers have been removed. 
        // Using shared helpers instead: ParamCoercion (for int/float/bool) and VectorParsing (for Vector2/3/4, Color)

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

        /// <summary>
        /// Resolves a type by name. Delegates to UnityTypeResolver.ResolveAny().
        /// </summary>
        private static Type ResolveType(string typeName)
        {
            return Helpers.UnityTypeResolver.ResolveAny(typeName);
        }
    }
}
