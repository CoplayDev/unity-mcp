using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Physics
{
    internal static class PhysicsForceOps
    {
        public static object ApplyForce(JObject @params)
        {
            var p = new ToolParams(@params);

            var targetResult = p.GetRequired("target");
            var errorObj = targetResult.GetOrError(out string targetStr);
            if (errorObj != null) return errorObj;

            string searchMethod = p.Get("search_method");

            GameObject go = FindTarget(@params["target"], searchMethod);
            if (go == null)
                return new ErrorResponse($"Target GameObject '{targetStr}' not found.");

            // Detect dimension
            string dimensionParam = p.Get("dimension")?.ToLowerInvariant();
            bool has3DRb = go.GetComponent<Rigidbody>() != null;
            bool has2DRb = go.GetComponent<Rigidbody2D>() != null;
            bool is2D;

            if (dimensionParam == "2d")
                is2D = true;
            else if (dimensionParam == "3d")
                is2D = false;
            else
                is2D = has2DRb && !has3DRb;

            // Validate rigidbody exists
            if (is2D && !has2DRb)
                return new ErrorResponse($"Target '{go.name}' has no Rigidbody2D. Add one before applying force.");
            if (!is2D && !has3DRb)
                return new ErrorResponse($"Target '{go.name}' has no Rigidbody. Add one before applying force.");

            // Validate not kinematic
            if (is2D)
            {
                var rb2d = go.GetComponent<Rigidbody2D>();
                if (rb2d.bodyType == RigidbodyType2D.Kinematic)
                    return new ErrorResponse($"Cannot apply force to kinematic Rigidbody on '{go.name}'.");
            }
            else
            {
                var rb = go.GetComponent<Rigidbody>();
                if (rb.isKinematic)
                    return new ErrorResponse($"Cannot apply force to kinematic Rigidbody on '{go.name}'.");
            }

            string forceType = (p.Get("force_type") ?? "normal").ToLowerInvariant();

            if (forceType == "explosion")
                return ApplyExplosionForce(p, go, is2D);

            if (forceType == "normal")
                return ApplyNormalForce(p, go, is2D);

            return new ErrorResponse($"Unknown force_type: '{forceType}'. Valid types: normal, explosion.");
        }

        private static object ApplyNormalForce(ToolParams p, GameObject go, bool is2D)
        {
            var forceToken = p.GetRaw("force") as JArray;
            var torqueToken = p.GetRaw("torque");

            if (forceToken == null && torqueToken == null)
                return new ErrorResponse("Either 'force' or 'torque' (or both) must be provided.");

            string modeStr = p.Get("force_mode");
            var positionToken = p.GetRaw("position") as JArray;

            var applied = new List<string>();
            var responseData = new Dictionary<string, object>
            {
                ["target"] = go.name,
                ["dimension"] = is2D ? "2d" : "3d",
                ["force_type"] = "normal"
            };

            if (is2D)
            {
                ForceMode2D mode2d = ForceMode2D.Force;
                if (!string.IsNullOrEmpty(modeStr))
                {
                    if (!Enum.TryParse<ForceMode2D>(modeStr, true, out mode2d))
                        return new ErrorResponse($"Invalid ForceMode2D: '{modeStr}'. Valid values: Force, Impulse.");

                    if (mode2d != ForceMode2D.Force && mode2d != ForceMode2D.Impulse)
                        return new ErrorResponse($"ForceMode2D only supports Force and Impulse, not '{modeStr}'.");
                }

                responseData["force_mode"] = mode2d.ToString();
                var rb2d = go.GetComponent<Rigidbody2D>();

                if (forceToken != null)
                {
                    if (forceToken.Count < 2)
                        return new ErrorResponse("'force' array must contain at least 2 floats for 2D.");

                    var forceVec = new Vector2(forceToken[0].Value<float>(), forceToken[1].Value<float>());

                    if (positionToken != null)
                    {
                        if (positionToken.Count < 2)
                            return new ErrorResponse("'position' array must contain at least 2 floats for 2D.");

                        var posVec = new Vector2(positionToken[0].Value<float>(), positionToken[1].Value<float>());
                        rb2d.AddForceAtPosition(forceVec, posVec, mode2d);
                    }
                    else
                    {
                        rb2d.AddForce(forceVec, mode2d);
                    }

                    responseData["force"] = new[] { forceVec.x, forceVec.y };
                    applied.Add("force");
                }

                if (torqueToken != null)
                {
                    float torqueFloat = torqueToken.Value<float>();
                    rb2d.AddTorque(torqueFloat, mode2d);
                    responseData["torque"] = torqueFloat;
                    applied.Add("torque");
                }
            }
            else
            {
                ForceMode mode = ForceMode.Force;
                if (!string.IsNullOrEmpty(modeStr))
                {
                    if (!Enum.TryParse<ForceMode>(modeStr, true, out mode))
                        return new ErrorResponse($"Invalid ForceMode: '{modeStr}'. Valid values: Force, Impulse, Acceleration, VelocityChange.");
                }

                responseData["force_mode"] = mode.ToString();
                var rb = go.GetComponent<Rigidbody>();

                if (forceToken != null)
                {
                    if (forceToken.Count < 3)
                        return new ErrorResponse("'force' array must contain at least 3 floats for 3D.");

                    var forceVec = new Vector3(
                        forceToken[0].Value<float>(),
                        forceToken[1].Value<float>(),
                        forceToken[2].Value<float>());

                    if (positionToken != null)
                    {
                        if (positionToken.Count < 3)
                            return new ErrorResponse("'position' array must contain at least 3 floats for 3D.");

                        var posVec = new Vector3(
                            positionToken[0].Value<float>(),
                            positionToken[1].Value<float>(),
                            positionToken[2].Value<float>());
                        rb.AddForceAtPosition(forceVec, posVec, mode);
                    }
                    else
                    {
                        rb.AddForce(forceVec, mode);
                    }

                    responseData["force"] = new[] { forceVec.x, forceVec.y, forceVec.z };
                    applied.Add("force");
                }

                if (torqueToken != null)
                {
                    var torqueArr = torqueToken as JArray;
                    if (torqueArr == null || torqueArr.Count < 3)
                        return new ErrorResponse("'torque' array must contain at least 3 floats for 3D.");

                    var torqueVec = new Vector3(
                        torqueArr[0].Value<float>(),
                        torqueArr[1].Value<float>(),
                        torqueArr[2].Value<float>());
                    rb.AddTorque(torqueVec, mode);
                    responseData["torque"] = new[] { torqueVec.x, torqueVec.y, torqueVec.z };
                    applied.Add("torque");
                }
            }

            string appliedStr = string.Join(" and ", applied);
            return new
            {
                success = true,
                message = $"Applied {appliedStr} to '{go.name}'.",
                data = responseData
            };
        }

        private static object ApplyExplosionForce(ToolParams p, GameObject go, bool is2D)
        {
            if (is2D)
                return new ErrorResponse("Explosion force is only available for 3D physics.");

            float? explosionForce = p.GetFloat("explosion_force");
            if (explosionForce == null)
                return new ErrorResponse("'explosion_force' is required for explosion force type.");

            var explosionPosToken = p.GetRaw("explosion_position") as JArray;
            if (explosionPosToken == null || explosionPosToken.Count < 3)
                return new ErrorResponse("'explosion_position' array (3 floats) is required for explosion force type.");

            float? explosionRadius = p.GetFloat("explosion_radius");
            if (explosionRadius == null)
                return new ErrorResponse("'explosion_radius' is required for explosion force type.");

            float upwardsModifier = p.GetFloat("upwards_modifier") ?? 0f;

            string modeStr = p.Get("force_mode");
            ForceMode mode = ForceMode.Force;
            if (!string.IsNullOrEmpty(modeStr))
            {
                if (!Enum.TryParse<ForceMode>(modeStr, true, out mode))
                    return new ErrorResponse($"Invalid ForceMode: '{modeStr}'. Valid values: Force, Impulse, Acceleration, VelocityChange.");
            }

            var explosionPos = new Vector3(
                explosionPosToken[0].Value<float>(),
                explosionPosToken[1].Value<float>(),
                explosionPosToken[2].Value<float>());

            var rb = go.GetComponent<Rigidbody>();
            rb.AddExplosionForce(explosionForce.Value, explosionPos, explosionRadius.Value, upwardsModifier, mode);

            return new
            {
                success = true,
                message = $"Applied explosion force to '{go.name}'.",
                data = new
                {
                    target = go.name,
                    dimension = "3d",
                    force_type = "explosion",
                    force_mode = mode.ToString(),
                    explosion_position = new[] { explosionPos.x, explosionPos.y, explosionPos.z },
                    explosion_force = explosionForce.Value,
                    explosion_radius = explosionRadius.Value,
                    upwards_modifier = upwardsModifier
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
    }
}
