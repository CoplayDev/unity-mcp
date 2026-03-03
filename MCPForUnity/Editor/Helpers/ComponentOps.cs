using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.Events;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Low-level component operations extracted from ManageGameObject and ManageComponents.
    /// Provides pure C# operations without JSON parsing or response formatting.
    /// </summary>
    public static class ComponentOps
    {
        /// <summary>
        /// Adds a component to a GameObject with Undo support.
        /// </summary>
        /// <param name="target">The target GameObject</param>
        /// <param name="componentType">The type of component to add</param>
        /// <param name="error">Error message if operation fails</param>
        /// <returns>The added component, or null if failed</returns>
        public static Component AddComponent(GameObject target, Type componentType, out string error)
        {
            error = null;

            if (target == null)
            {
                error = "Target GameObject is null.";
                return null;
            }

            if (componentType == null || !typeof(Component).IsAssignableFrom(componentType))
            {
                error = $"Type '{componentType?.Name ?? "null"}' is not a valid Component type.";
                return null;
            }

            // Prevent adding duplicate Transform
            if (componentType == typeof(Transform))
            {
                error = "Cannot add another Transform component.";
                return null;
            }

            // Check for 2D/3D physics conflicts
            string conflictError = CheckPhysicsConflict(target, componentType);
            if (conflictError != null)
            {
                error = conflictError;
                return null;
            }

            // Produce a clearer error when this component already exists and cannot be duplicated.
            Component existingComponent = target.GetComponent(componentType);
            if (existingComponent != null && !AllowsMultiple(target, componentType))
            {
                error = $"Component '{componentType.Name}' already exists on '{target.name}' and this type does not allow multiple instances.";
                return null;
            }

            try
            {
                Component newComponent = Undo.AddComponent(target, componentType);
                if (newComponent == null)
                {
                    if (target.GetComponent(componentType) != null && !AllowsMultiple(target, componentType))
                    {
                        error = $"Component '{componentType.Name}' already exists on '{target.name}' and this type does not allow multiple instances.";
                    }
                    else
                    {
                        error = $"Failed to add component '{componentType.Name}' to '{target.name}'. Unity may restrict this component on the current target.";
                    }
                    return null;
                }

                // Apply default values for specific component types
                ApplyDefaultValues(newComponent);

                return newComponent;
            }
            catch (Exception ex)
            {
                error = $"Error adding component '{componentType.Name}': {ex.Message}";
                return null;
            }
        }

        /// <summary>
        /// Removes a component from a GameObject with Undo support.
        /// </summary>
        /// <param name="target">The target GameObject</param>
        /// <param name="componentType">The type of component to remove</param>
        /// <param name="error">Error message if operation fails</param>
        /// <returns>True if component was removed successfully</returns>
        public static bool RemoveComponent(GameObject target, Type componentType, out string error)
        {
            error = null;

            if (target == null)
            {
                error = "Target GameObject is null.";
                return false;
            }

            if (componentType == null)
            {
                error = "Component type is null.";
                return false;
            }

            // Prevent removing Transform
            if (componentType == typeof(Transform))
            {
                error = "Cannot remove Transform component.";
                return false;
            }

            Component component = target.GetComponent(componentType);
            if (component == null)
            {
                error = $"Component '{componentType.Name}' not found on '{target.name}'.";
                return false;
            }

            try
            {
                Undo.DestroyObjectImmediate(component);
                return true;
            }
            catch (Exception ex)
            {
                error = $"Error removing component '{componentType.Name}': {ex.Message}";
                return false;
            }
        }

        /// <summary>
        /// Sets a property value on a component using reflection.
        /// </summary>
        /// <param name="component">The target component</param>
        /// <param name="propertyName">The property or field name</param>
        /// <param name="value">The value to set (JToken)</param>
        /// <param name="error">Error message if operation fails</param>
        /// <returns>True if property was set successfully</returns>
        public static bool SetProperty(Component component, string propertyName, JToken value, out string error)
        {
            error = null;

            if (component == null)
            {
                error = "Component is null.";
                return false;
            }

            if (string.IsNullOrEmpty(propertyName))
            {
                error = "Property name is null or empty.";
                return false;
            }

            Type type = component.GetType();
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;
            string normalizedName = ParamCoercion.NormalizePropertyName(propertyName);

            // UnityEventBase-derived types must be set via SerializedProperty, not reflection.
            // Reflection creates a disconnected object that Unity's serialization layer doesn't track,
            // causing m_PersistentCalls to be empty when the scene is saved.
            Type memberType = ResolveMemberType(type, propertyName, normalizedName);
            if (memberType != null && typeof(UnityEventBase).IsAssignableFrom(memberType))
            {
                return SetViaSerializedProperty(component, propertyName, normalizedName, value, out error);
            }

            // Try reflection first (property, field, then non-public serialized field)
            if (TrySetViaReflection(component, type, propertyName, normalizedName, flags, value, out error))
                return true;

            // Reflection failed — fall back to SerializedProperty which handles arrays,
            // custom serialization (e.g. UdonSharp), and types reflection can't convert.
            string reflectionError = error;
            if (SetViaSerializedProperty(component, propertyName, normalizedName, value, out error))
                return true;

            // Both paths failed. If reflection found the member but couldn't convert,
            // report that (more useful than the SerializedProperty error).
            // If reflection didn't find it at all, report the SerializedProperty error.
            if (reflectionError != null && !reflectionError.Contains("not found"))
                error = reflectionError;

            return false;
        }

        private static bool TrySetViaReflection(object component, Type type, string propertyName, string normalizedName, BindingFlags flags, JToken value, out string error)
        {
            error = null;

            // Try property first
            PropertyInfo propInfo = type.GetProperty(propertyName, flags)
                                 ?? type.GetProperty(normalizedName, flags);
            if (propInfo != null && propInfo.CanWrite)
            {
                try
                {
                    object convertedValue = PropertyConversion.ConvertToType(value, propInfo.PropertyType);
                    if (convertedValue == null && value.Type != JTokenType.Null)
                    {
                        error = $"Failed to convert value for property '{propertyName}' to type '{propInfo.PropertyType.Name}'.";
                        return false;
                    }
                    propInfo.SetValue(component, convertedValue);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Failed to set property '{propertyName}': {ex.Message}";
                    return false;
                }
            }

            // Try field
            FieldInfo fieldInfo = type.GetField(propertyName, flags)
                               ?? type.GetField(normalizedName, flags);
            if (fieldInfo != null && !fieldInfo.IsInitOnly)
            {
                try
                {
                    object convertedValue = PropertyConversion.ConvertToType(value, fieldInfo.FieldType);
                    if (convertedValue == null && value.Type != JTokenType.Null)
                    {
                        error = $"Failed to convert value for field '{propertyName}' to type '{fieldInfo.FieldType.Name}'.";
                        return false;
                    }
                    fieldInfo.SetValue(component, convertedValue);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Failed to set field '{propertyName}': {ex.Message}";
                    return false;
                }
            }

            // Try non-public serialized fields — traverse inheritance hierarchy
            fieldInfo = FindSerializedFieldInHierarchy(type, propertyName)
                     ?? FindSerializedFieldInHierarchy(type, normalizedName);
            if (fieldInfo != null)
            {
                try
                {
                    object convertedValue = PropertyConversion.ConvertToType(value, fieldInfo.FieldType);
                    if (convertedValue == null && value.Type != JTokenType.Null)
                    {
                        error = $"Failed to convert value for serialized field '{propertyName}' to type '{fieldInfo.FieldType.Name}'.";
                        return false;
                    }
                    fieldInfo.SetValue(component, convertedValue);
                    return true;
                }
                catch (Exception ex)
                {
                    error = $"Failed to set serialized field '{propertyName}': {ex.Message}";
                    return false;
                }
            }

            error = $"Property or field '{propertyName}' not found on component '{type.Name}'.";
            return false;
        }

        /// <summary>
        /// Gets all public properties and fields from a component type.
        /// </summary>
        public static List<string> GetAccessibleMembers(Type componentType)
        {
            var members = new List<string>();
            if (componentType == null) return members;

            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance;

            foreach (var prop in componentType.GetProperties(flags))
            {
                if (prop.CanWrite && prop.GetSetMethod() != null)
                {
                    members.Add(prop.Name);
                }
            }

            foreach (var field in componentType.GetFields(flags))
            {
                if (!field.IsInitOnly)
                {
                    members.Add(field.Name);
                }
            }

            // Include private [SerializeField] fields - traverse inheritance hierarchy
            // Type.GetFields with NonPublic only returns fields declared directly on that type,
            // so we need to walk up the chain to find inherited private serialized fields
            var seenFieldNames = new HashSet<string>(members); // Avoid duplicates with public fields
            Type currentType = componentType;
            while (currentType != null && currentType != typeof(object))
            {
                foreach (var field in currentType.GetFields(BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly))
                {
                    if (field.GetCustomAttribute<SerializeField>() != null && !seenFieldNames.Contains(field.Name))
                    {
                        members.Add(field.Name);
                        seenFieldNames.Add(field.Name);
                    }
                }
                currentType = currentType.BaseType;
            }

            members.Sort();
            return members;
        }

        // --- Private Helpers ---

        /// <summary>
        /// Searches for a non-public [SerializeField] field through the entire inheritance hierarchy.
        /// Type.GetField() with NonPublic only returns fields declared directly on that type,
        /// so this method walks up the chain to find inherited private serialized fields.
        /// </summary>
        internal static FieldInfo FindSerializedFieldInHierarchy(Type type, string fieldName)
        {
            if (type == null || string.IsNullOrEmpty(fieldName))
                return null;

            BindingFlags privateFlags = BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;
            Type currentType = type;

            // Walk up the inheritance chain
            while (currentType != null && currentType != typeof(object))
            {
                // Search for the field on this specific type (case-insensitive)
                foreach (var field in currentType.GetFields(privateFlags))
                {
                    if (string.Equals(field.Name, fieldName, StringComparison.OrdinalIgnoreCase) &&
                        field.GetCustomAttribute<SerializeField>() != null)
                    {
                        return field;
                    }
                }
                currentType = currentType.BaseType;
            }

            return null;
        }

        private static string CheckPhysicsConflict(GameObject target, Type componentType)
        {
            bool isAdding2DPhysics =
                typeof(Rigidbody2D).IsAssignableFrom(componentType) ||
                typeof(Collider2D).IsAssignableFrom(componentType);

            bool isAdding3DPhysics =
                typeof(Rigidbody).IsAssignableFrom(componentType) ||
                typeof(Collider).IsAssignableFrom(componentType);

            if (isAdding2DPhysics)
            {
                if (target.GetComponent<Rigidbody>() != null || target.GetComponent<Collider>() != null)
                {
                    return $"Cannot add 2D physics component '{componentType.Name}' because the GameObject '{target.name}' already has a 3D Rigidbody or Collider.";
                }
            }
            else if (isAdding3DPhysics)
            {
                if (target.GetComponent<Rigidbody2D>() != null || target.GetComponent<Collider2D>() != null)
                {
                    return $"Cannot add 3D physics component '{componentType.Name}' because the GameObject '{target.name}' already has a 2D Rigidbody or Collider.";
                }
            }

            return null;
        }

        private static void ApplyDefaultValues(Component component)
        {
            // Default newly added Lights to Directional
            if (component is Light light)
            {
                light.type = LightType.Directional;
            }
        }

        private static bool AllowsMultiple(GameObject target, Type componentType)
        {
            if (target == null || componentType == null)
            {
                return false;
            }

            if (Attribute.IsDefined(componentType, typeof(DisallowMultipleComponent), inherit: true))
            {
                return false;
            }

            return true;
        }

        // --- UnityEvent SerializedProperty support ---

        private static Type ResolveMemberType(Type componentType, string propertyName, string normalizedName)
        {
            BindingFlags flags = BindingFlags.Public | BindingFlags.Instance | BindingFlags.IgnoreCase;

            PropertyInfo propInfo = componentType.GetProperty(propertyName, flags)
                                 ?? componentType.GetProperty(normalizedName, flags);
            if (propInfo != null)
                return propInfo.PropertyType;

            FieldInfo fieldInfo = componentType.GetField(propertyName, flags)
                               ?? componentType.GetField(normalizedName, flags);
            if (fieldInfo != null)
                return fieldInfo.FieldType;

            fieldInfo = FindSerializedFieldInHierarchy(componentType, propertyName)
                     ?? FindSerializedFieldInHierarchy(componentType, normalizedName);
            if (fieldInfo != null)
                return fieldInfo.FieldType;

            return null;
        }

        private static bool SetViaSerializedProperty(Component component, string propertyName, string normalizedName, JToken value, out string error)
        {
            error = null;
            using var so = new SerializedObject(component);

            SerializedProperty prop = so.FindProperty(propertyName)
                                   ?? so.FindProperty(normalizedName);
            if (prop == null)
            {
                error = $"SerializedProperty '{propertyName}' not found on component '{component.GetType().Name}'.";
                return false;
            }

            if (!SetSerializedPropertyRecursive(prop, value, out error, 0))
                return false;

            so.ApplyModifiedProperties();
            return true;
        }

        private static bool SetSerializedPropertyRecursive(SerializedProperty prop, JToken value, out string error, int depth)
        {
            error = null;
            const int MaxDepth = 20;
            if (depth > MaxDepth)
            {
                error = $"Maximum recursion depth ({MaxDepth}) exceeded.";
                return false;
            }

            try
            {
                // Array + JArray
                if (prop.isArray && prop.propertyType != SerializedPropertyType.String && value is JArray jArray)
                {
                    prop.arraySize = jArray.Count;
                    prop.serializedObject.ApplyModifiedProperties();
                    prop.serializedObject.Update();

                    for (int i = 0; i < jArray.Count; i++)
                    {
                        var element = prop.GetArrayElementAtIndex(i);
                        if (!SetSerializedPropertyRecursive(element, jArray[i], out error, depth + 1))
                            return false;
                    }
                    return true;
                }

                // Generic (struct/class) + JObject
                if (prop.propertyType == SerializedPropertyType.Generic && !prop.isArray && value is JObject jObj)
                {
                    foreach (var kvp in jObj)
                    {
                        var child = FindPropertyRelativeFuzzy(prop, kvp.Key);
                        if (child == null)
                        {
                            error = $"Sub-property '{kvp.Key}' not found under '{prop.propertyPath}'.";
                            return false;
                        }
                        if (!SetSerializedPropertyRecursive(child, kvp.Value, out error, depth + 1))
                            return false;
                    }
                    return true;
                }

                // ObjectReference
                if (prop.propertyType == SerializedPropertyType.ObjectReference)
                    return SetObjectReference(prop, value, out error);

                // Leaf types
                switch (prop.propertyType)
                {
                    case SerializedPropertyType.Integer:
                        int intVal = ParamCoercion.CoerceInt(value, int.MinValue);
                        if (intVal == int.MinValue && value?.Type != JTokenType.Integer)
                        {
                            if (value == null || value.Type == JTokenType.Null ||
                                (value.Type == JTokenType.String && !int.TryParse(value.ToString(), out _)))
                            {
                                error = "Expected integer value.";
                                return false;
                            }
                        }
                        prop.intValue = intVal;
                        return true;

                    case SerializedPropertyType.Boolean:
                        if (value == null || value.Type == JTokenType.Null)
                        {
                            error = "Expected boolean value.";
                            return false;
                        }
                        prop.boolValue = ParamCoercion.CoerceBool(value, false);
                        return true;

                    case SerializedPropertyType.Float:
                        float floatVal = ParamCoercion.CoerceFloat(value, float.NaN);
                        if (float.IsNaN(floatVal))
                        {
                            error = "Expected float value.";
                            return false;
                        }
                        prop.floatValue = floatVal;
                        return true;

                    case SerializedPropertyType.String:
                        prop.stringValue = value == null || value.Type == JTokenType.Null ? string.Empty : value.ToString();
                        return true;

                    case SerializedPropertyType.Enum:
                        return SetEnum(prop, value, out error);

                    default:
                        error = $"Unsupported SerializedPropertyType: {prop.propertyType} at '{prop.propertyPath}'.";
                        return false;
                }
            }
            catch (Exception ex)
            {
                error = $"Error setting '{prop.propertyPath}': {ex.Message}";
                return false;
            }
        }

        private static bool SetObjectReference(SerializedProperty prop, JToken value, out string error)
        {
            error = null;

            if (value == null || value.Type == JTokenType.Null)
            {
                prop.objectReferenceValue = null;
                return true;
            }

            if (value.Type == JTokenType.Integer)
            {
                int id = value.Value<int>();
                var resolved = EditorUtility.InstanceIDToObject(id);
                if (resolved == null)
                {
                    error = $"No object found with instanceID {id}.";
                    return false;
                }
                prop.objectReferenceValue = resolved;
                return true;
            }

            if (value is JObject jObj)
            {
                var idToken = jObj["instanceID"];
                if (idToken != null)
                {
                    int id = ParamCoercion.CoerceInt(idToken, 0);
                    var resolved = EditorUtility.InstanceIDToObject(id);
                    if (resolved == null)
                    {
                        error = $"No object found with instanceID {id}.";
                        return false;
                    }
                    prop.objectReferenceValue = resolved;
                    return true;
                }

                var guidToken = jObj["guid"];
                if (guidToken != null)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guidToken.ToString());
                    if (string.IsNullOrEmpty(path))
                    {
                        error = $"No asset found for GUID '{guidToken}'.";
                        return false;
                    }
                    prop.objectReferenceValue = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(path);
                    return true;
                }

                var pathToken = jObj["path"];
                if (pathToken != null)
                {
                    string sanitized = AssetPathUtility.SanitizeAssetPath(pathToken.ToString());
                    var resolved = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sanitized);
                    if (resolved == null)
                    {
                        error = $"No asset found at path '{pathToken}'.";
                        return false;
                    }
                    prop.objectReferenceValue = resolved;
                    return true;
                }

                var nameToken = jObj["name"];
                if (nameToken != null)
                {
                    return ResolveSceneObjectByName(prop, nameToken.ToString(), out error);
                }

                error = "Object reference must contain 'instanceID', 'guid', 'path', or 'name'.";
                return false;
            }

            if (value.Type == JTokenType.String)
            {
                string strVal = value.ToString();
                if (strVal.StartsWith("Assets/", StringComparison.OrdinalIgnoreCase) || strVal.Contains("/"))
                {
                    string sanitized = AssetPathUtility.SanitizeAssetPath(strVal);
                    var resolved = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(sanitized);
                    if (resolved == null)
                    {
                        error = $"No asset found at path '{strVal}'.";
                        return false;
                    }
                    prop.objectReferenceValue = resolved;
                    return true;
                }

                // Fall back to scene hierarchy lookup by name.
                return ResolveSceneObjectByName(prop, strVal, out error);
            }

            error = $"Unsupported object reference format: {value.Type}.";
            return false;
        }

        /// <summary>
        /// Resolves a scene GameObject by name and assigns it (or a component on it)
        /// to a SerializedProperty. Uses GameObjectLookup for robust search
        /// including inactive objects and prefab stage support.
        /// </summary>
        private static bool ResolveSceneObjectByName(SerializedProperty prop, string name, out string error)
        {
            error = null;
            if (string.IsNullOrWhiteSpace(name))
            {
                error = "Cannot resolve object reference from empty name.";
                return false;
            }

            var ids = GameObjectLookup.SearchGameObjects(
                GameObjectLookup.SearchMethod.ByName, name, includeInactive: true, maxResults: 1);

            if (ids.Count == 0)
            {
                error = $"No GameObject named '{name}' found in scene.";
                return false;
            }

            var go = GameObjectLookup.FindById(ids[0]);
            if (go == null)
            {
                error = $"GameObject '{name}' found but could not be resolved.";
                return false;
            }

            // If the property accepts a GameObject directly, assign it.
            prop.objectReferenceValue = go;
            if (prop.objectReferenceValue != null)
                return true;

            // The field type may expect a specific Component (e.g. Transform, Rigidbody).
            // Try each component on the GameObject until one is accepted.
            var components = go.GetComponents<Component>();
            foreach (var comp in components)
            {
                if (comp == null) continue;
                prop.objectReferenceValue = comp;
                if (prop.objectReferenceValue != null)
                    return true;
            }

            error = $"GameObject '{name}' found but no compatible component for property type.";
            return false;
        }

        /// <summary>
        /// Finds a child SerializedProperty by name, falling back to underscore-insensitive matching.
        /// The batch_execute transport can strip underscores from JSON keys
        /// (e.g. m_PersistentCalls → mPersistentCalls), so we iterate immediate children
        /// and compare with underscores removed.
        /// </summary>
        private static SerializedProperty FindPropertyRelativeFuzzy(SerializedProperty parent, string key)
        {
            var child = parent.FindPropertyRelative(key);
            if (child != null) return child;

            string normalizedKey = key.Replace("_", "").ToLowerInvariant();

            var end = parent.GetEndProperty();
            var iter = parent.Copy();
            if (!iter.Next(true)) return null;

            while (!SerializedProperty.EqualContents(iter, end))
            {
                if (iter.depth == parent.depth + 1)
                {
                    string normalizedName = iter.name.Replace("_", "").ToLowerInvariant();
                    if (normalizedName == normalizedKey)
                        return parent.FindPropertyRelative(iter.name);
                }
                if (!iter.Next(false))
                    break;
            }

            return null;
        }

        private static bool SetEnum(SerializedProperty prop, JToken value, out string error)
        {
            error = null;
            var names = prop.enumNames;
            if (names == null || names.Length == 0)
            {
                error = "Enum has no names.";
                return false;
            }

            if (value.Type == JTokenType.Integer)
            {
                int idx = value.Value<int>();
                if (idx < 0 || idx >= names.Length)
                {
                    error = $"Enum index out of range: {idx}.";
                    return false;
                }
                prop.enumValueIndex = idx;
                return true;
            }

            string s = value.ToString();
            for (int i = 0; i < names.Length; i++)
            {
                if (string.Equals(names[i], s, StringComparison.OrdinalIgnoreCase))
                {
                    prop.enumValueIndex = i;
                    return true;
                }
            }
            error = $"Unknown enum name '{s}'.";
            return false;
        }
    }
}

