using System;
using System.Collections.Generic;
using System.Reflection;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Physics
{
    internal static class JointOps
    {
        private static readonly Dictionary<string, Type> JointTypes3D = new Dictionary<string, Type>
        {
            { "fixed", typeof(FixedJoint) },
            { "hinge", typeof(HingeJoint) },
            { "spring", typeof(SpringJoint) },
            { "character", typeof(CharacterJoint) },
            { "configurable", typeof(ConfigurableJoint) }
        };

        private static readonly Dictionary<string, Type> JointTypes2D = new Dictionary<string, Type>
        {
            { "distance", typeof(DistanceJoint2D) },
            { "fixed", typeof(FixedJoint2D) },
            { "friction", typeof(FrictionJoint2D) },
            { "hinge", typeof(HingeJoint2D) },
            { "relative", typeof(RelativeJoint2D) },
            { "slider", typeof(SliderJoint2D) },
            { "spring", typeof(SpringJoint2D) },
            { "target", typeof(TargetJoint2D) },
            { "wheel", typeof(WheelJoint2D) }
        };

        public static object AddJoint(JObject @params)
        {
            var p = new ToolParams(@params);

            var targetResult = p.GetRequired("target");
            var errorObj = targetResult.GetOrError(out string targetStr);
            if (errorObj != null) return errorObj;

            var jointTypeResult = p.GetRequired("joint_type");
            errorObj = jointTypeResult.GetOrError(out string jointTypeStr);
            if (errorObj != null) return errorObj;

            string searchMethod = p.Get("search_method");

            GameObject go = FindTarget(@params["target"], searchMethod);
            if (go == null)
                return new ErrorResponse($"Target GameObject '{targetStr}' not found.");

            string dimensionParam = p.Get("dimension")?.ToLowerInvariant();
            bool is3D = go.GetComponent<Rigidbody>() != null;
            bool has2DRb = go.GetComponent<Rigidbody2D>() != null;
            bool is2D;

            if (dimensionParam == "2d")
            {
                if (!has2DRb)
                    return new ErrorResponse($"Target '{go.name}' has no Rigidbody2D.");
                is2D = true;
            }
            else if (dimensionParam == "3d")
            {
                if (!is3D)
                    return new ErrorResponse($"Target '{go.name}' has no Rigidbody.");
                is2D = false;
            }
            else if (!string.IsNullOrEmpty(dimensionParam))
            {
                return new ErrorResponse($"Invalid dimension: '{dimensionParam}'. Use '3d' or '2d'.");
            }
            else
            {
                // Auto-detect; if both, prefer 3D (3D is the default physics)
                is2D = has2DRb && !is3D;
            }

            if (!is2D && !is3D)
                return new ErrorResponse($"Target '{go.name}' has no Rigidbody or Rigidbody2D. Add one before adding a joint.");

            string key = jointTypeStr.ToLowerInvariant();
            var typeMap = is2D ? JointTypes2D : JointTypes3D;

            if (!typeMap.TryGetValue(key, out Type jointComponentType))
            {
                string validTypes = string.Join(", ", typeMap.Keys);
                string dimension = is2D ? "2D" : "3D";
                return new ErrorResponse($"Unknown {dimension} joint type: '{jointTypeStr}'. Valid types: {validTypes}.");
            }

            // Validate connected body before mutating the scene
            string connectedBodyTarget = p.Get("connected_body");
            GameObject connectedGo = null;
            if (!string.IsNullOrEmpty(connectedBodyTarget))
            {
                connectedGo = FindTarget(new JValue(connectedBodyTarget), searchMethod);
                if (connectedGo == null)
                    return new ErrorResponse($"Connected body GameObject '{connectedBodyTarget}' not found.");

                if (is2D)
                {
                    if (connectedGo.GetComponent<Rigidbody2D>() == null)
                        return new ErrorResponse($"Connected body '{connectedGo.name}' has no Rigidbody2D.");
                }
                else
                {
                    if (connectedGo.GetComponent<Rigidbody>() == null)
                        return new ErrorResponse($"Connected body '{connectedGo.name}' has no Rigidbody.");
                }
            }

            var joint = Undo.AddComponent(go, jointComponentType);
            if (joint == null)
                return new ErrorResponse($"Failed to add {jointComponentType.Name} to '{go.name}'.");

            // Set connected body now that the joint is created
            if (connectedGo != null)
            {
                if (is2D)
                    ((Joint2D)joint).connectedBody = connectedGo.GetComponent<Rigidbody2D>();
                else
                    ((Joint)joint).connectedBody = connectedGo.GetComponent<Rigidbody>();
            }

            // Set properties via reflection if provided
            var properties = p.GetRaw("properties") as JObject;
            if (properties != null)
            {
                SetPropertiesViaReflection(joint, properties);
            }

            EditorUtility.SetDirty(go);

            return new
            {
                success = true,
                message = $"{jointComponentType.Name} added to '{go.name}'.",
                data = new
                {
                    jointType = jointComponentType.Name,
                    instanceID = joint.GetInstanceID(),
                    gameObjectInstanceID = go.GetInstanceID()
                }
            };
        }

        public static object ConfigureJoint(JObject @params)
        {
            var p = new ToolParams(@params);

            var targetResult = p.GetRequired("target");
            var errorObj = targetResult.GetOrError(out string targetStr);
            if (errorObj != null) return errorObj;

            string searchMethod = p.Get("search_method");

            GameObject go = FindTarget(@params["target"], searchMethod);
            if (go == null)
                return new ErrorResponse($"Target GameObject '{targetStr}' not found.");

            string jointTypeStr = p.Get("joint_type");
            Component joint = ResolveJoint(go, jointTypeStr);
            if (joint == null)
            {
                if (!string.IsNullOrEmpty(jointTypeStr))
                    return new ErrorResponse($"No joint of type '{jointTypeStr}' found on '{go.name}'.");

                // Check if multiple joints exist to give a better error
                var joints3D = go.GetComponents<Joint>();
                var joints2D = go.GetComponents<Joint2D>();
                int total = joints3D.Length + joints2D.Length;
                if (total > 1)
                    return new ErrorResponse($"Multiple joints found on '{go.name}' ({total} total). Specify 'joint_type' to target a specific joint.");

                return new ErrorResponse($"No joint found on '{go.name}'.");
            }

            Undo.RecordObject(joint, $"Configure {joint.GetType().Name}");

            var configured = new List<string>();

            // Motor configuration (HingeJoint)
            var motorToken = p.GetRaw("motor") as JObject;
            if (motorToken != null)
            {
                if (joint is HingeJoint hingeForMotor)
                {
                    var motor = hingeForMotor.motor;
                    if (motorToken["targetVelocity"] != null)
                        motor.targetVelocity = motorToken["targetVelocity"].Value<float>();
                    if (motorToken["force"] != null)
                        motor.force = motorToken["force"].Value<float>();
                    if (motorToken["freeSpin"] != null)
                        motor.freeSpin = motorToken["freeSpin"].Value<bool>();
                    hingeForMotor.motor = motor;
                    hingeForMotor.useMotor = true;
                    configured.Add("motor");
                }
                else
                {
                    return new ErrorResponse($"Motor configuration is only supported on HingeJoint, not {joint.GetType().Name}.");
                }
            }

            // Limits configuration (HingeJoint)
            var limitsToken = p.GetRaw("limits") as JObject;
            if (limitsToken != null)
            {
                if (joint is HingeJoint hingeForLimits)
                {
                    var limits = hingeForLimits.limits;
                    if (limitsToken["min"] != null)
                        limits.min = limitsToken["min"].Value<float>();
                    if (limitsToken["max"] != null)
                        limits.max = limitsToken["max"].Value<float>();
                    if (limitsToken["bounciness"] != null)
                        limits.bounciness = limitsToken["bounciness"].Value<float>();
                    hingeForLimits.limits = limits;
                    hingeForLimits.useLimits = true;
                    configured.Add("limits");
                }
                else
                {
                    return new ErrorResponse($"Limits configuration is only supported on HingeJoint, not {joint.GetType().Name}.");
                }
            }

            // Spring configuration (HingeJoint / SpringJoint)
            var springToken = p.GetRaw("spring") as JObject;
            if (springToken != null)
            {
                if (joint is HingeJoint hingeForSpring)
                {
                    var spring = hingeForSpring.spring;
                    if (springToken["spring"] != null)
                        spring.spring = springToken["spring"].Value<float>();
                    if (springToken["damper"] != null)
                        spring.damper = springToken["damper"].Value<float>();
                    if (springToken["targetPosition"] != null)
                        spring.targetPosition = springToken["targetPosition"].Value<float>();
                    hingeForSpring.spring = spring;
                    hingeForSpring.useSpring = true;
                    configured.Add("spring");
                }
                else if (joint is SpringJoint springJoint)
                {
                    if (springToken["spring"] != null)
                        springJoint.spring = springToken["spring"].Value<float>();
                    if (springToken["damper"] != null)
                        springJoint.damper = springToken["damper"].Value<float>();
                    if (springToken["targetPosition"] != null)
                        springJoint.minDistance = springToken["targetPosition"].Value<float>();
                    configured.Add("spring");
                }
                else
                {
                    return new ErrorResponse($"Spring configuration is only supported on HingeJoint/SpringJoint, not {joint.GetType().Name}.");
                }
            }

            // Drive configuration (ConfigurableJoint)
            var driveToken = p.GetRaw("drive") as JObject;
            if (driveToken != null)
            {
                if (joint is ConfigurableJoint configJoint)
                {
                    var xDriveToken = driveToken["xDrive"] as JObject;
                    if (xDriveToken != null)
                    {
                        var xDrive = configJoint.xDrive;
                        if (xDriveToken["positionSpring"] != null)
                            xDrive.positionSpring = xDriveToken["positionSpring"].Value<float>();
                        if (xDriveToken["positionDamper"] != null)
                            xDrive.positionDamper = xDriveToken["positionDamper"].Value<float>();
                        if (xDriveToken["maximumForce"] != null)
                            xDrive.maximumForce = xDriveToken["maximumForce"].Value<float>();
                        configJoint.xDrive = xDrive;
                    }
                    configured.Add("drive");
                }
                else
                {
                    return new ErrorResponse($"Drive configuration is only supported on ConfigurableJoint, not {joint.GetType().Name}.");
                }
            }

            // Direct property setting
            var properties = p.GetRaw("properties") as JObject;
            if (properties != null)
            {
                SetPropertiesViaReflection(joint, properties);
                configured.Add("properties");
            }

            EditorUtility.SetDirty(joint);

            return new
            {
                success = true,
                message = $"Configured {joint.GetType().Name} on '{go.name}': {string.Join(", ", configured)}.",
                data = new
                {
                    jointType = joint.GetType().Name,
                    configured,
                    instanceID = joint.GetInstanceID()
                }
            };
        }

        public static object RemoveJoint(JObject @params)
        {
            var p = new ToolParams(@params);

            var targetResult = p.GetRequired("target");
            var errorObj = targetResult.GetOrError(out string targetStr);
            if (errorObj != null) return errorObj;

            string searchMethod = p.Get("search_method");

            GameObject go = FindTarget(@params["target"], searchMethod);
            if (go == null)
                return new ErrorResponse($"Target GameObject '{targetStr}' not found.");

            string jointTypeStr = p.Get("joint_type");

            var jointsToRemove = new List<Component>();

            if (!string.IsNullOrEmpty(jointTypeStr))
            {
                // Remove specific joint type
                bool is2D = go.GetComponent<Rigidbody2D>() != null;
                var typeMap = is2D ? JointTypes2D : JointTypes3D;
                string key = jointTypeStr.ToLowerInvariant();

                if (!typeMap.TryGetValue(key, out Type jointComponentType))
                {
                    string validTypes = string.Join(", ", typeMap.Keys);
                    string dimension = is2D ? "2D" : "3D";
                    return new ErrorResponse($"Unknown {dimension} joint type: '{jointTypeStr}'. Valid types: {validTypes}.");
                }

                var components = go.GetComponents(jointComponentType);
                jointsToRemove.AddRange(components);
            }
            else
            {
                // Remove ALL joints (both 3D and 2D)
                var joints3D = go.GetComponents<Joint>();
                var joints2D = go.GetComponents<Joint2D>();
                jointsToRemove.AddRange(joints3D);
                jointsToRemove.AddRange(joints2D);
            }

            if (jointsToRemove.Count == 0)
            {
                string typeInfo = string.IsNullOrEmpty(jointTypeStr) ? "" : $" of type '{jointTypeStr}'";
                return new ErrorResponse($"No joints{typeInfo} found on '{go.name}'.");
            }

            int removedCount = 0;
            foreach (var joint in jointsToRemove)
            {
                Undo.DestroyObjectImmediate(joint);
                removedCount++;
            }

            EditorUtility.SetDirty(go);

            return new
            {
                success = true,
                message = $"Removed {removedCount} joint(s) from '{go.name}'.",
                data = new
                {
                    removedCount,
                    gameObjectInstanceID = go.GetInstanceID()
                }
            };
        }

        private static GameObject FindTarget(JToken targetToken, string searchMethod)
        {
            if (targetToken == null)
                return null;

            if (targetToken.Type == JTokenType.Integer)
            {
                int instanceId = targetToken.Value<int>();
                return GameObjectLookup.FindById(instanceId);
            }

            string targetStr = targetToken.ToString();

            if (int.TryParse(targetStr, out int parsedId))
            {
                var byId = GameObjectLookup.FindById(parsedId);
                if (byId != null)
                    return byId;
            }

            return GameObjectLookup.FindByTarget(targetToken, searchMethod ?? "by_name", true);
        }

        private static Component ResolveJoint(GameObject go, string jointTypeStr)
        {
            if (!string.IsNullOrEmpty(jointTypeStr))
            {
                bool is2D = go.GetComponent<Rigidbody2D>() != null;
                var typeMap = is2D ? JointTypes2D : JointTypes3D;
                string key = jointTypeStr.ToLowerInvariant();

                if (typeMap.TryGetValue(key, out Type jointType))
                    return go.GetComponent(jointType);

                return null;
            }

            // Auto-detect: find the single joint on the GO
            var joints3D = go.GetComponents<Joint>();
            var joints2D = go.GetComponents<Joint2D>();

            int totalCount = joints3D.Length + joints2D.Length;
            if (totalCount == 1)
                return joints3D.Length == 1 ? (Component)joints3D[0] : joints2D[0];

            return null;
        }

        private static void SetPropertiesViaReflection(Component component, JObject properties)
        {
            var type = component.GetType();
            foreach (var prop in properties.Properties())
            {
                var propInfo = type.GetProperty(prop.Name, BindingFlags.Public | BindingFlags.Instance);
                if (propInfo != null && propInfo.CanWrite)
                {
                    try
                    {
                        object value = ConvertValue(prop.Value, propInfo.PropertyType);
                        propInfo.SetValue(component, value);
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"[JointOps] Failed to set property '{prop.Name}': {ex.Message}");
                    }
                    continue;
                }

                var fieldInfo = type.GetField(prop.Name, BindingFlags.Public | BindingFlags.Instance);
                if (fieldInfo != null)
                {
                    try
                    {
                        object value = ConvertValue(prop.Value, fieldInfo.FieldType);
                        fieldInfo.SetValue(component, value);
                    }
                    catch (Exception ex)
                    {
                        McpLog.Warn($"[JointOps] Failed to set field '{prop.Name}': {ex.Message}");
                    }
                }
            }
        }

        private static object ConvertValue(JToken token, Type targetType)
        {
            if (targetType == typeof(float))
                return token.Value<float>();
            if (targetType == typeof(int))
                return token.Value<int>();
            if (targetType == typeof(bool))
                return token.Value<bool>();
            if (targetType == typeof(string))
                return token.Value<string>();
            if (targetType == typeof(Vector3))
            {
                var arr = token as JArray;
                if (arr != null && arr.Count >= 3)
                    return new Vector3(arr[0].Value<float>(), arr[1].Value<float>(), arr[2].Value<float>());
            }
            if (targetType == typeof(Vector2))
            {
                var arr = token as JArray;
                if (arr != null && arr.Count >= 2)
                    return new Vector2(arr[0].Value<float>(), arr[1].Value<float>());
            }

            return token.ToObject(targetType);
        }
    }
}
