using System;
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools
{
    [McpForUnityTool("manage_material", AutoRegister = false)]
    public static class ManageMaterial
    {
        public static object HandleCommand(JObject @params)
        {
            string action = @params["action"]?.ToString()?.ToLowerInvariant();
            if (string.IsNullOrEmpty(action))
            {
                return new ErrorResponse("Action is required");
            }

            try
            {
                switch (action)
                {
                    case "ping":
                        return new SuccessResponse("pong", new { tool = "manage_material" });

                    case "create":
                        return CreateMaterial(@params);

                    case "set_material_shader_property":
                        return SetMaterialShaderProperty(@params);

                    case "set_material_color":
                        return SetMaterialColor(@params);

                    case "assign_material_to_renderer":
                        return AssignMaterialToRenderer(@params);

                    case "set_renderer_color":
                        return SetRendererColor(@params);

                    case "get_material_info":
                        return GetMaterialInfo(@params);

                    default:
                        return new ErrorResponse($"Unknown action: {action}");
                }
            }
            catch (Exception ex)
            {
                return new ErrorResponse(ex.Message, new { stackTrace = ex.StackTrace });
            }
        }

        private static string NormalizePath(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;

            // Normalize separators and ensure Assets/ root
            path = AssetPathUtility.SanitizeAssetPath(path);

            // Ensure .mat extension
            if (!path.EndsWith(".mat", StringComparison.OrdinalIgnoreCase))
            {
                path += ".mat";
            }

            return path;
        }

        private static object SetMaterialShaderProperty(JObject @params)
        {
            string materialPath = NormalizePath(@params["materialPath"]?.ToString());
            string property = @params["property"]?.ToString();
            JToken value = @params["value"];

            if (string.IsNullOrEmpty(materialPath) || string.IsNullOrEmpty(property) || value == null)
            {
                return new ErrorResponse("materialPath, property, and value are required");
            }

            // Find material
            var findInstruction = new JObject { ["find"] = materialPath };
            Material mat = ObjectResolver.Resolve(findInstruction, typeof(Material)) as Material;

            if (mat == null)
            {
                return new ErrorResponse($"Could not find material at path: {materialPath}");
            }

            Undo.RecordObject(mat, "Set Material Property");

            // Normalize alias/casing once for all code paths
            property = MaterialOps.ResolvePropertyName(mat, property);

            // 1. Try handling Texture instruction explicitly (ManageMaterial special feature)
            if (value.Type == JTokenType.Object)
            {
                // Check if it looks like an instruction
                if (value is JObject obj && (obj.ContainsKey("find") || obj.ContainsKey("method")))
                {
                    Texture tex = ObjectResolver.Resolve(obj, typeof(Texture)) as Texture;
                    if (tex != null && mat.HasProperty(property))
                    {
                        mat.SetTexture(property, tex);
                        EditorUtility.SetDirty(mat);
                        return new SuccessResponse($"Set texture property {property} on {mat.name}");
                    }
                }
            }

            // 2. Fallback to standard logic via MaterialOps (handles Colors, Floats, Strings->Path)
            bool success = MaterialOps.TrySetShaderProperty(mat, property, value, UnityJsonSerializer.Instance);

            if (success)
            {
                EditorUtility.SetDirty(mat);
                return new SuccessResponse($"Set property {property} on {mat.name}");
            }
            else
            {
                return new ErrorResponse($"Failed to set property {property}. Value format might be unsupported or texture not found.");
            }
        }

        private static object SetMaterialColor(JObject @params)
        {
            string materialPath = NormalizePath(@params["materialPath"]?.ToString());
            JToken colorToken = @params["color"];
            string property = @params["property"]?.ToString();

            if (string.IsNullOrEmpty(materialPath) || colorToken == null)
            {
                return new ErrorResponse("materialPath and color are required");
            }

            var findInstruction = new JObject { ["find"] = materialPath };
            Material mat = ObjectResolver.Resolve(findInstruction, typeof(Material)) as Material;

            if (mat == null)
            {
                return new ErrorResponse($"Could not find material at path: {materialPath}");
            }

            Color color;
            try
            {
                color = MaterialOps.ParseColor(colorToken, UnityJsonSerializer.Instance);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Invalid color format: {e.Message}");
            }

            Undo.RecordObject(mat, "Set Material Color");

            bool foundProp = false;
            if (!string.IsNullOrEmpty(property))
            {
                if (mat.HasProperty(property))
                {
                    mat.SetColor(property, color);
                    foundProp = true;
                }
            }
            else
            {
                // Fallback logic: _BaseColor (URP/HDRP) then _Color (Built-in)
                if (mat.HasProperty("_BaseColor"))
                {
                    mat.SetColor("_BaseColor", color);
                    foundProp = true;
                    property = "_BaseColor";
                }
                else if (mat.HasProperty("_Color"))
                {
                    mat.SetColor("_Color", color);
                    foundProp = true;
                    property = "_Color";
                }
            }

            if (foundProp)
            {
                EditorUtility.SetDirty(mat);
                return new SuccessResponse($"Set color on {property}");
            }
            else
            {
                return new ErrorResponse("Could not find suitable color property (_BaseColor or _Color) or specified property does not exist.");
            }
        }

        private static object AssignMaterialToRenderer(JObject @params)
        {
            string target = @params["target"]?.ToString();
            string searchMethod = @params["searchMethod"]?.ToString();
            string materialPath = NormalizePath(@params["materialPath"]?.ToString());
            int slot = @params["slot"]?.ToObject<int>() ?? 0;

            if (string.IsNullOrEmpty(target) || string.IsNullOrEmpty(materialPath))
            {
                return new ErrorResponse("target and materialPath are required");
            }

            var goInstruction = new JObject { ["find"] = target };
            if (!string.IsNullOrEmpty(searchMethod)) goInstruction["method"] = searchMethod;

            GameObject go = ObjectResolver.Resolve(goInstruction, typeof(GameObject)) as GameObject;
            if (go == null)
            {
                return new ErrorResponse($"Could not find target GameObject: {target}");
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                return new ErrorResponse($"GameObject {go.name} has no Renderer component");
            }

            var matInstruction = new JObject { ["find"] = materialPath };
            Material mat = ObjectResolver.Resolve(matInstruction, typeof(Material)) as Material;
            if (mat == null)
            {
                return new ErrorResponse($"Could not find material: {materialPath}");
            }

            Undo.RecordObject(renderer, "Assign Material");

            Material[] sharedMats = renderer.sharedMaterials;
            if (slot < 0 || slot >= sharedMats.Length)
            {
                return new ErrorResponse($"Slot {slot} out of bounds (count: {sharedMats.Length})");
            }

            sharedMats[slot] = mat;
            renderer.sharedMaterials = sharedMats;

            EditorUtility.SetDirty(renderer);
            return new SuccessResponse($"Assigned material {mat.name} to {go.name} slot {slot}");
        }

        private static object SetRendererColor(JObject @params)
        {
            string target = @params["target"]?.ToString();
            string searchMethod = @params["searchMethod"]?.ToString();
            JToken colorToken = @params["color"];
            int slot = @params["slot"]?.ToObject<int>() ?? 0;
            string mode = @params["mode"]?.ToString() ?? "property_block";

            if (string.IsNullOrEmpty(target) || colorToken == null)
            {
                return new ErrorResponse("target and color are required");
            }

            Color color;
            try
            {
                color = MaterialOps.ParseColor(colorToken, UnityJsonSerializer.Instance);
            }
            catch (Exception e)
            {
                return new ErrorResponse($"Invalid color format: {e.Message}");
            }

            var goInstruction = new JObject { ["find"] = target };
            if (!string.IsNullOrEmpty(searchMethod)) goInstruction["method"] = searchMethod;

            GameObject go = ObjectResolver.Resolve(goInstruction, typeof(GameObject)) as GameObject;
            if (go == null)
            {
                return new ErrorResponse($"Could not find target GameObject: {target}");
            }

            Renderer renderer = go.GetComponent<Renderer>();
            if (renderer == null)
            {
                return new ErrorResponse($"GameObject {go.name} has no Renderer component");
            }

            if (mode == "property_block")
            {
                if (slot < 0 || slot >= renderer.sharedMaterials.Length)
                {
                    return new ErrorResponse($"Slot {slot} out of bounds (count: {renderer.sharedMaterials.Length})");
                }

                MaterialPropertyBlock block = new MaterialPropertyBlock();
                renderer.GetPropertyBlock(block, slot);

                if (renderer.sharedMaterials[slot] != null)
                {
                    Material mat = renderer.sharedMaterials[slot];
                    if (mat.HasProperty("_BaseColor")) block.SetColor("_BaseColor", color);
                    else if (mat.HasProperty("_Color")) block.SetColor("_Color", color);
                    else block.SetColor("_Color", color);
                }
                else
                {
                    block.SetColor("_Color", color);
                }

                renderer.SetPropertyBlock(block, slot);
                EditorUtility.SetDirty(renderer);
                return new SuccessResponse($"Set renderer color (PropertyBlock) on slot {slot}");
            }
            else if (mode == "shared")
            {
                if (slot >= 0 && slot < renderer.sharedMaterials.Length)
                {
                    Material mat = renderer.sharedMaterials[slot];
                    if (mat == null)
                    {
                        return new ErrorResponse($"No material in slot {slot}");
                    }
                    Undo.RecordObject(mat, "Set Material Color");
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                    else mat.SetColor("_Color", color);
                    EditorUtility.SetDirty(mat);
                    return new SuccessResponse("Set shared material color");
                }
                return new ErrorResponse("Invalid slot");
            }
            else if (mode == "instance")
            {
                if (slot >= 0 && slot < renderer.materials.Length)
                {
                    Material mat = renderer.materials[slot];
                    if (mat == null)
                    {
                        return new ErrorResponse($"No material in slot {slot}");
                    }
                    // Note: Undo cannot fully revert material instantiation
                    Undo.RecordObject(mat, "Set Instance Material Color");
                    if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
                    else mat.SetColor("_Color", color);
                    return new SuccessResponse("Set instance material color", new { warning = "Material instance created; Undo cannot fully revert instantiation." });
                }
                return new ErrorResponse("Invalid slot");
            }

            return new ErrorResponse($"Unknown mode: {mode}");
        }

        private static object GetMaterialInfo(JObject @params)
        {
            string materialPath = NormalizePath(@params["materialPath"]?.ToString());
            if (string.IsNullOrEmpty(materialPath))
            {
                return new ErrorResponse("materialPath is required");
            }

            var findInstruction = new JObject { ["find"] = materialPath };
            Material mat = ObjectResolver.Resolve(findInstruction, typeof(Material)) as Material;

            if (mat == null)
            {
                return new ErrorResponse($"Could not find material at path: {materialPath}");
            }

            Shader shader = mat.shader;
            var properties = new List<object>();

#if UNITY_6000_0_OR_NEWER
            int propertyCount = shader.GetPropertyCount();
            for (int i = 0; i < propertyCount; i++)
            {
                string name = shader.GetPropertyName(i);
                var type = shader.GetPropertyType(i);
                string description = shader.GetPropertyDescription(i);

                object currentValue = null;
                try
                {
                    if (mat.HasProperty(name))
                    {
                        switch (type)
                        {
                            case UnityEngine.Rendering.ShaderPropertyType.Color:
                                var c = mat.GetColor(name);
                                currentValue = new { r = c.r, g = c.g, b = c.b, a = c.a };
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Vector:
                                var v = mat.GetVector(name);
                                currentValue = new { x = v.x, y = v.y, z = v.z, w = v.w };
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Float:
                            case UnityEngine.Rendering.ShaderPropertyType.Range:
                                currentValue = mat.GetFloat(name);
                                break;
                            case UnityEngine.Rendering.ShaderPropertyType.Texture:
                                currentValue = mat.GetTexture(name)?.name ?? "null";
                                break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    currentValue = $"<error: {ex.Message}>";
                }

                properties.Add(new
                {
                    name = name,
                    type = type.ToString(),
                    description = description,
                    value = currentValue
                });
            }
#else
            int propertyCount = ShaderUtil.GetPropertyCount(shader);
            for (int i = 0; i < propertyCount; i++)
            {
                string name = ShaderUtil.GetPropertyName(shader, i);
                ShaderUtil.ShaderPropertyType type = ShaderUtil.GetPropertyType(shader, i);
                string description = ShaderUtil.GetPropertyDescription(shader, i);

                object currentValue = null;
                try
                {
                    if (mat.HasProperty(name))
                    {
                        switch (type)
                        {
                            case ShaderUtil.ShaderPropertyType.Color:
                                var c = mat.GetColor(name);
                                currentValue = new { r = c.r, g = c.g, b = c.b, a = c.a };
                                break;
                            case ShaderUtil.ShaderPropertyType.Vector:
                                var v = mat.GetVector(name);
                                currentValue = new { x = v.x, y = v.y, z = v.z, w = v.w };
                                break;
                            case ShaderUtil.ShaderPropertyType.Float: currentValue = mat.GetFloat(name); break;
                            case ShaderUtil.ShaderPropertyType.Range: currentValue = mat.GetFloat(name); break;
                            case ShaderUtil.ShaderPropertyType.TexEnv: currentValue = mat.GetTexture(name)?.name ?? "null"; break;
                        }
                    }
                }
                catch (Exception ex)
                {
                    currentValue = $"<error: {ex.Message}>";
                }

                properties.Add(new
                {
                    name = name,
                    type = type.ToString(),
                    description = description,
                    value = currentValue
                });
            }
#endif

            return new SuccessResponse($"Retrieved material info for {mat.name}", new
            {
                material = mat.name,
                shader = shader.name,
                properties = properties
            });
        }

        private static object CreateMaterial(JObject @params)
        {
            string materialPath = NormalizePath(@params["materialPath"]?.ToString());
            string shaderName = @params["shader"]?.ToString() ?? "Standard";
            JToken colorToken = @params["color"];
            string colorProperty = @params["property"]?.ToString();

            JObject properties = null;
            JToken propsToken = @params["properties"];
            if (propsToken != null)
            {
                if (propsToken.Type == JTokenType.String)
                {
                    try { properties = JObject.Parse(propsToken.ToString()); }
                    catch (Exception ex) { return new ErrorResponse($"Invalid JSON in properties: {ex.Message}"); }
                }
                else if (propsToken is JObject obj)
                {
                    properties = obj;
                }
            }

            if (string.IsNullOrEmpty(materialPath))
            {
                return new ErrorResponse("materialPath is required");
            }

            // Safety check: SanitizeAssetPath should guarantee Assets/ prefix
            // This check catches edge cases where normalization might fail
            if (!materialPath.StartsWith("Assets/"))
            {
                return new ErrorResponse($"Invalid path '{materialPath}'. Path must be within Assets/ folder.");
            }

            Shader shader = RenderPipelineUtility.ResolveShader(shaderName);
            if (shader == null)
            {
                return new ErrorResponse($"Could not find shader: {shaderName}");
            }

            // Check for existing asset to avoid silent overwrite
            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) != null)
            {
                return new ErrorResponse($"Material already exists at {materialPath}");
            }

            Material material = null;
            var shouldDestroyMaterial = true;
            try
            {
                material = new Material(shader);

                // Apply color param during creation (keeps Python tool signature and C# implementation consistent).
                // If "properties" already contains a color property, let properties win.
                bool shouldApplyColor = false;
                if (colorToken != null)
                {
                    if (properties == null)
                    {
                        shouldApplyColor = true;
                    }
                    else if (!string.IsNullOrEmpty(colorProperty))
                    {
                        // If colorProperty is specified, only check that specific property.
                        shouldApplyColor = !properties.ContainsKey(colorProperty);
                    }
                    else
                    {
                        // If colorProperty is not specified, check fallback properties.
                        shouldApplyColor = !properties.ContainsKey("_BaseColor") && !properties.ContainsKey("_Color");
                    }
                }

                if (shouldApplyColor)
                {
                    Color color;
                    try
                    {
                        color = MaterialOps.ParseColor(colorToken, UnityJsonSerializer.Instance);
                    }
                    catch (Exception e)
                    {
                        return new ErrorResponse($"Invalid color format: {e.Message}");
                    }

                    if (!string.IsNullOrEmpty(colorProperty))
                    {
                        if (material.HasProperty(colorProperty))
                        {
                            material.SetColor(colorProperty, color);
                        }
                        else
                        {
                            return new ErrorResponse($"Specified color property '{colorProperty}' does not exist on this material.");
                        }
                    }
                    else if (material.HasProperty("_BaseColor"))
                    {
                        material.SetColor("_BaseColor", color);
                    }
                    else if (material.HasProperty("_Color"))
                    {
                        material.SetColor("_Color", color);
                    }
                    else
                    {
                        return new ErrorResponse("Could not find suitable color property (_BaseColor or _Color) on this material's shader.");
                    }
                }

                AssetDatabase.CreateAsset(material, materialPath);
                shouldDestroyMaterial = false; // material is now owned by the AssetDatabase

                if (properties != null)
                {
                    MaterialOps.ApplyProperties(material, properties, UnityJsonSerializer.Instance);
                }

                EditorUtility.SetDirty(material);
                AssetDatabase.SaveAssets();

                return new SuccessResponse($"Created material at {materialPath} with shader {shaderName}");
            }
            finally
            {
                if (shouldDestroyMaterial && material != null)
                {
                    UnityEngine.Object.DestroyImmediate(material);
                }
            }
        }
    }
}
