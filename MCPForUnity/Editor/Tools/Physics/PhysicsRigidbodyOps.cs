using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Runtime.Helpers;

namespace MCPForUnity.Editor.Tools.Physics
{
    internal static class PhysicsRigidbodyOps
    {
        private static readonly HashSet<string> Valid3DKeys = new HashSet<string>
        {
            "mass", "drag", "lineardamping", "angulardrag", "angulardamping",
            "usegravity", "iskinematic", "interpolation", "collisiondetectionmode", "constraints"
        };

        private static readonly HashSet<string> Valid2DKeys = new HashSet<string>
        {
            "mass", "gravityscale", "drag", "lineardamping", "angulardrag", "angulardamping",
            "bodytype", "simulated", "collisiondetectionmode", "constraints"
        };

        public static object GetRigidbody(JObject @params)
        {
            var p = new ToolParams(@params);

            var targetToken = p.GetRaw("target");
            if (targetToken == null || string.IsNullOrEmpty(targetToken.ToString()))
                return new ErrorResponse("'target' parameter is required.");

            string searchMethod = p.Get("search_method");
            string dimensionParam = p.Get("dimension")?.ToLowerInvariant();

            var go = GameObjectLookup.FindByTarget(targetToken, searchMethod ?? "by_name");
            if (go == null)
                return new ErrorResponse($"GameObject not found: '{targetToken}'.");

            bool has3D = go.GetComponent<Rigidbody>() != null;
            bool has2D = go.GetComponent<Rigidbody2D>() != null;

            if (!has3D && !has2D)
                return new ErrorResponse($"No Rigidbody or Rigidbody2D found on '{go.name}'.");

            bool is2D;
            if (dimensionParam == "2d")
                is2D = true;
            else if (dimensionParam == "3d")
                is2D = false;
            else
                is2D = has2D && !has3D;

            if (is2D)
                return GetRigidbody2D(go);

            return GetRigidbody3D(go);
        }

        private static object GetRigidbody3D(GameObject go)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                return new ErrorResponse($"No Rigidbody found on '{go.name}'.");

            var data = new Dictionary<string, object>
            {
                ["target"] = go.name,
                ["instanceID"] = go.GetInstanceIDCompat(),
                ["dimension"] = "3d",
                ["mass"] = rb.mass,
#if UNITY_6000_0_OR_NEWER
                ["linearDamping"] = rb.linearDamping,
                ["angularDamping"] = rb.angularDamping,
#else
                ["linearDamping"] = rb.drag,
                ["angularDamping"] = rb.angularDrag,
#endif
                ["useGravity"] = rb.useGravity,
                ["isKinematic"] = rb.isKinematic,
                ["position"] = new[] { rb.position.x, rb.position.y, rb.position.z },
                ["rotation"] = new[] { rb.rotation.x, rb.rotation.y, rb.rotation.z, rb.rotation.w },
#if UNITY_6000_0_OR_NEWER
                ["velocity"] = new[] { rb.linearVelocity.x, rb.linearVelocity.y, rb.linearVelocity.z },
#else
                ["velocity"] = new[] { rb.velocity.x, rb.velocity.y, rb.velocity.z },
#endif
                ["angularVelocity"] = new[] { rb.angularVelocity.x, rb.angularVelocity.y, rb.angularVelocity.z },
                ["interpolation"] = rb.interpolation.ToString(),
                ["collisionDetectionMode"] = rb.collisionDetectionMode.ToString(),
                ["constraints"] = rb.constraints.ToString(),
                ["isSleeping"] = rb.IsSleeping(),
                ["centerOfMass"] = new[] { rb.centerOfMass.x, rb.centerOfMass.y, rb.centerOfMass.z },
                ["maxAngularVelocity"] = rb.maxAngularVelocity,
#if UNITY_6000_0_OR_NEWER
                ["maxLinearVelocity"] = rb.maxLinearVelocity,
#endif
            };

            return new
            {
                success = true,
                message = $"Retrieved Rigidbody state for '{go.name}'.",
                data
            };
        }

        private static object GetRigidbody2D(GameObject go)
        {
            var rb2d = go.GetComponent<Rigidbody2D>();
            if (rb2d == null)
                return new ErrorResponse($"No Rigidbody2D found on '{go.name}'.");

            var data = new Dictionary<string, object>
            {
                ["target"] = go.name,
                ["instanceID"] = go.GetInstanceIDCompat(),
                ["dimension"] = "2d",
                ["mass"] = rb2d.mass,
                ["gravityScale"] = rb2d.gravityScale,
#if UNITY_6000_0_OR_NEWER
                ["linearDamping"] = rb2d.linearDamping,
                ["angularDamping"] = rb2d.angularDamping,
#else
                ["linearDamping"] = rb2d.drag,
                ["angularDamping"] = rb2d.angularDrag,
#endif
                ["bodyType"] = rb2d.bodyType.ToString(),
                ["simulated"] = rb2d.simulated,
                ["position"] = new[] { rb2d.position.x, rb2d.position.y },
                ["rotation"] = rb2d.rotation,
#if UNITY_6000_0_OR_NEWER
                ["velocity"] = new[] { rb2d.linearVelocity.x, rb2d.linearVelocity.y },
#else
                ["velocity"] = new[] { rb2d.velocity.x, rb2d.velocity.y },
#endif
                ["angularVelocity"] = rb2d.angularVelocity,
                ["collisionDetectionMode"] = rb2d.collisionDetectionMode.ToString(),
                ["constraints"] = rb2d.constraints.ToString(),
                ["isSleeping"] = rb2d.IsSleeping(),
                ["isAwake"] = rb2d.IsAwake(),
                ["centerOfMass"] = new[] { rb2d.centerOfMass.x, rb2d.centerOfMass.y },
            };

            return new
            {
                success = true,
                message = $"Retrieved Rigidbody2D state for '{go.name}'.",
                data
            };
        }

        public static object ConfigureRigidbody(JObject @params)
        {
            var p = new ToolParams(@params);

            var targetToken = p.GetRaw("target");
            if (targetToken == null || string.IsNullOrEmpty(targetToken.ToString()))
                return new ErrorResponse("'target' parameter is required.");

            var properties = p.GetRaw("properties") as JObject;
            if (properties == null || properties.Count == 0)
                return new ErrorResponse("'properties' parameter is required and must be a non-empty object.");

            string searchMethod = p.Get("search_method");
            string dimensionParam = p.Get("dimension")?.ToLowerInvariant();

            var go = GameObjectLookup.FindByTarget(targetToken, searchMethod ?? "by_name");
            if (go == null)
                return new ErrorResponse($"GameObject not found: '{targetToken}'.");

            bool has3D = go.GetComponent<Rigidbody>() != null;
            bool has2D = go.GetComponent<Rigidbody2D>() != null;
            bool is2D;

            if (dimensionParam == "2d")
                is2D = true;
            else if (dimensionParam == "3d")
                is2D = false;
            else
                is2D = has2D && !has3D;

            if (is2D)
                return ConfigureRigidbody2D(go, properties);

            return ConfigureRigidbody3D(go, properties);
        }

        private static object ConfigureRigidbody3D(GameObject go, JObject properties)
        {
            var rb = go.GetComponent<Rigidbody>();
            if (rb == null)
                return new ErrorResponse($"No Rigidbody found on '{go.name}'.");

            // Validate all keys before applying any changes
            var unknown = new List<string>();
            foreach (var prop in properties.Properties())
            {
                string key = prop.Name.ToLowerInvariant().Replace("_", "");
                if (!Valid3DKeys.Contains(key))
                    unknown.Add(prop.Name);
            }
            if (unknown.Count > 0)
                return new ErrorResponse(
                    $"Unknown Rigidbody property(ies): {string.Join(", ", unknown)}.");

            Undo.RecordObject(rb, "Configure Rigidbody");

            var changed = new List<string>();

            foreach (var prop in properties.Properties())
            {
                string key = prop.Name.ToLowerInvariant().Replace("_", "");
                switch (key)
                {
                    case "mass":
                        rb.mass = prop.Value.Value<float>();
                        changed.Add("mass");
                        break;
                    case "drag":
                    case "lineardamping":
#if UNITY_6000_0_OR_NEWER
                        rb.linearDamping = prop.Value.Value<float>();
#else
                        rb.drag = prop.Value.Value<float>();
#endif
                        changed.Add("linearDamping");
                        break;
                    case "angulardrag":
                    case "angulardamping":
#if UNITY_6000_0_OR_NEWER
                        rb.angularDamping = prop.Value.Value<float>();
#else
                        rb.angularDrag = prop.Value.Value<float>();
#endif
                        changed.Add("angularDamping");
                        break;
                    case "usegravity":
                        rb.useGravity = prop.Value.Value<bool>();
                        changed.Add("useGravity");
                        break;
                    case "iskinematic":
                        rb.isKinematic = prop.Value.Value<bool>();
                        changed.Add("isKinematic");
                        break;
                    case "interpolation":
                    {
                        string val = prop.Value.ToString();
                        if (Enum.TryParse<RigidbodyInterpolation>(val, true, out var interp))
                        {
                            rb.interpolation = interp;
                            changed.Add("interpolation");
                        }
                        else
                        {
                            return new ErrorResponse(
                                $"Invalid interpolation value: '{val}'. Valid: None, Interpolate, Extrapolate.");
                        }
                        break;
                    }
                    case "collisiondetectionmode":
                    {
                        string val = prop.Value.ToString();
                        if (Enum.TryParse<CollisionDetectionMode>(val, true, out var mode))
                        {
                            rb.collisionDetectionMode = mode;
                            changed.Add("collisionDetectionMode");
                        }
                        else
                        {
                            return new ErrorResponse(
                                $"Invalid collisionDetectionMode: '{val}'. Valid: Discrete, Continuous, ContinuousDynamic, ContinuousSpeculative.");
                        }
                        break;
                    }
                    case "constraints":
                    {
                        var token = prop.Value;
                        if (token.Type == JTokenType.Integer)
                        {
                            rb.constraints = (RigidbodyConstraints)token.Value<int>();
                            changed.Add("constraints");
                        }
                        else
                        {
                            string val = token.ToString();
                            if (Enum.TryParse<RigidbodyConstraints>(val, true, out var c))
                            {
                                rb.constraints = c;
                                changed.Add("constraints");
                            }
                            else
                            {
                                return new ErrorResponse(
                                    $"Invalid constraints value: '{val}'. Use enum name (e.g. 'FreezePositionX, FreezeRotationY') or int flags.");
                            }
                        }
                        break;
                    }
                }
            }

            EditorUtility.SetDirty(rb);

            return new
            {
                success = true,
                message = $"Configured Rigidbody on '{go.name}': {string.Join(", ", changed)}.",
                data = new { target = go.name, dimension = "3d", changed }
            };
        }

        private static object ConfigureRigidbody2D(GameObject go, JObject properties)
        {
            var rb2d = go.GetComponent<Rigidbody2D>();
            if (rb2d == null)
                return new ErrorResponse($"No Rigidbody2D found on '{go.name}'.");

            // Validate all keys before applying any changes
            var unknown = new List<string>();
            foreach (var prop in properties.Properties())
            {
                string key = prop.Name.ToLowerInvariant().Replace("_", "");
                if (!Valid2DKeys.Contains(key))
                    unknown.Add(prop.Name);
            }
            if (unknown.Count > 0)
                return new ErrorResponse(
                    $"Unknown Rigidbody2D property(ies): {string.Join(", ", unknown)}.");

            Undo.RecordObject(rb2d, "Configure Rigidbody2D");

            var changed = new List<string>();

            foreach (var prop in properties.Properties())
            {
                string key = prop.Name.ToLowerInvariant().Replace("_", "");
                switch (key)
                {
                    case "mass":
                        rb2d.mass = prop.Value.Value<float>();
                        changed.Add("mass");
                        break;
                    case "gravityscale":
                        rb2d.gravityScale = prop.Value.Value<float>();
                        changed.Add("gravityScale");
                        break;
                    case "drag":
                    case "lineardamping":
#if UNITY_6000_0_OR_NEWER
                        rb2d.linearDamping = prop.Value.Value<float>();
#else
                        rb2d.drag = prop.Value.Value<float>();
#endif
                        changed.Add("linearDamping");
                        break;
                    case "angulardrag":
                    case "angulardamping":
#if UNITY_6000_0_OR_NEWER
                        rb2d.angularDamping = prop.Value.Value<float>();
#else
                        rb2d.angularDrag = prop.Value.Value<float>();
#endif
                        changed.Add("angularDamping");
                        break;
                    case "bodytype":
                    {
                        string val = prop.Value.ToString();
                        if (Enum.TryParse<RigidbodyType2D>(val, true, out var bt))
                        {
                            rb2d.bodyType = bt;
                            changed.Add("bodyType");
                        }
                        else
                        {
                            return new ErrorResponse(
                                $"Invalid bodyType: '{val}'. Valid: Dynamic, Kinematic, Static.");
                        }
                        break;
                    }
                    case "simulated":
                        rb2d.simulated = prop.Value.Value<bool>();
                        changed.Add("simulated");
                        break;
                    case "collisiondetectionmode":
                    {
                        string val = prop.Value.ToString();
                        if (Enum.TryParse<CollisionDetectionMode2D>(val, true, out var mode))
                        {
                            rb2d.collisionDetectionMode = mode;
                            changed.Add("collisionDetectionMode");
                        }
                        else
                        {
                            return new ErrorResponse(
                                $"Invalid collisionDetectionMode: '{val}'. Valid: Discrete, Continuous.");
                        }
                        break;
                    }
                    case "constraints":
                    {
                        var token = prop.Value;
                        if (token.Type == JTokenType.Integer)
                        {
                            rb2d.constraints = (RigidbodyConstraints2D)token.Value<int>();
                            changed.Add("constraints");
                        }
                        else
                        {
                            string val = token.ToString();
                            if (Enum.TryParse<RigidbodyConstraints2D>(val, true, out var c))
                            {
                                rb2d.constraints = c;
                                changed.Add("constraints");
                            }
                            else
                            {
                                return new ErrorResponse(
                                    $"Invalid constraints value: '{val}'. Use enum name (e.g. 'FreezePositionX, FreezeRotation') or int flags.");
                            }
                        }
                        break;
                    }
                }
            }

            EditorUtility.SetDirty(rb2d);

            return new
            {
                success = true,
                message = $"Configured Rigidbody2D on '{go.name}': {string.Join(", ", changed)}.",
                data = new { target = go.name, dimension = "2d", changed }
            };
        }
    }
}
