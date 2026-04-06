using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;
using UnityEngine;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnity.Editor.Tools.Physics
{
    internal static class PhysicsQueryOps
    {
        public static object Raycast(JObject @params)
        {
            var p = new ToolParams(@params);
            string dimension = (p.Get("dimension") ?? "3d").ToLowerInvariant();

            var originArr = p.GetRaw("origin") as JArray;
            if (originArr == null)
                return new ErrorResponse("'origin' parameter is required (array of floats).");

            var dirArr = p.GetRaw("direction") as JArray;
            if (dirArr == null)
                return new ErrorResponse("'direction' parameter is required (array of floats).");

            float maxDistance = p.GetFloat("max_distance") ?? Mathf.Infinity;

            if (dimension == "2d")
                return Raycast2D(originArr, dirArr, maxDistance, p);

            if (dimension != "3d")
                return new ErrorResponse($"Invalid dimension: '{dimension}'. Use '3d' or '2d'.");

            return Raycast3D(originArr, dirArr, maxDistance, p);
        }

        private static object Raycast3D(JArray originArr, JArray dirArr, float maxDistance, ToolParams p)
        {
            if (originArr.Count < 3)
                return new ErrorResponse("3D raycast 'origin' requires [x, y, z].");
            if (dirArr.Count < 3)
                return new ErrorResponse("3D raycast 'direction' requires [x, y, z].");

            var origin = new Vector3(
                originArr[0].Value<float>(), originArr[1].Value<float>(), originArr[2].Value<float>());
            var direction = new Vector3(
                dirArr[0].Value<float>(), dirArr[1].Value<float>(), dirArr[2].Value<float>());

            int layerMask = ResolveLayerMask(p.Get("layer_mask"));

            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal;
            string qti = p.Get("query_trigger_interaction");
            if (!string.IsNullOrEmpty(qti))
            {
                if (!System.Enum.TryParse(qti, true, out triggerInteraction))
                    return new ErrorResponse($"Invalid query_trigger_interaction: '{qti}'. Valid: UseGlobal, Ignore, Collide.");
            }

            UnityEngine.Physics.SyncTransforms();

            bool hit = UnityEngine.Physics.Raycast(origin, direction, out RaycastHit hitInfo, maxDistance, layerMask, triggerInteraction);

            if (!hit)
            {
                return new
                {
                    success = true,
                    message = "Raycast did not hit anything.",
                    data = new { hit = false }
                };
            }

            return new
            {
                success = true,
                message = $"Raycast hit '{hitInfo.collider.gameObject.name}'.",
                data = new
                {
                    hit = true,
                    point = new[] { hitInfo.point.x, hitInfo.point.y, hitInfo.point.z },
                    normal = new[] { hitInfo.normal.x, hitInfo.normal.y, hitInfo.normal.z },
                    distance = hitInfo.distance,
                    gameObject = hitInfo.collider.gameObject.name,
                    instanceID = hitInfo.collider.gameObject.GetInstanceID(),
                    collider_type = hitInfo.collider.GetType().Name
                }
            };
        }

        private static object Raycast2D(JArray originArr, JArray dirArr, float maxDistance, ToolParams p)
        {
            if (originArr.Count < 2)
                return new ErrorResponse("2D raycast 'origin' requires [x, y].");
            if (dirArr.Count < 2)
                return new ErrorResponse("2D raycast 'direction' requires [x, y].");

            var origin = new Vector2(originArr[0].Value<float>(), originArr[1].Value<float>());
            var direction = new Vector2(dirArr[0].Value<float>(), dirArr[1].Value<float>());

            int layerMask = ResolveLayerMask(p.Get("layer_mask"));

            Physics2D.SyncTransforms();

            var hit = Physics2D.Raycast(origin, direction, maxDistance, layerMask);

            if (hit.collider == null)
            {
                return new
                {
                    success = true,
                    message = "2D Raycast did not hit anything.",
                    data = new { hit = false }
                };
            }

            return new
            {
                success = true,
                message = $"2D Raycast hit '{hit.collider.gameObject.name}'.",
                data = new
                {
                    hit = true,
                    point = new[] { hit.point.x, hit.point.y },
                    normal = new[] { hit.normal.x, hit.normal.y },
                    distance = hit.distance,
                    gameObject = hit.collider.gameObject.name,
                    instanceID = hit.collider.gameObject.GetInstanceID(),
                    collider_type = hit.collider.GetType().Name
                }
            };
        }

        public static object Overlap(JObject @params)
        {
            var p = new ToolParams(@params);
            string dimension = (p.Get("dimension") ?? "3d").ToLowerInvariant();
            string shape = p.Get("shape");
            if (string.IsNullOrEmpty(shape))
                return new ErrorResponse("'shape' parameter is required (sphere, box, capsule for 3D; circle, box, capsule for 2D).");

            var posArr = p.GetRaw("position") as JArray;
            if (posArr == null)
                return new ErrorResponse("'position' parameter is required (array of floats).");

            var sizeToken = p.GetRaw("size");
            if (sizeToken == null)
                return new ErrorResponse("'size' parameter is required.");

            int layerMask = ResolveLayerMask(p.Get("layer_mask"));

            if (dimension == "2d")
                return Overlap2D(shape, posArr, sizeToken, layerMask);

            if (dimension != "3d")
                return new ErrorResponse($"Invalid dimension: '{dimension}'. Use '3d' or '2d'.");

            return Overlap3D(shape, posArr, sizeToken, layerMask);
        }

        private static object Overlap3D(string shape, JArray posArr, JToken sizeToken, int layerMask)
        {
            if (posArr.Count < 3)
                return new ErrorResponse("3D overlap 'position' requires [x, y, z].");

            var position = new Vector3(
                posArr[0].Value<float>(), posArr[1].Value<float>(), posArr[2].Value<float>());

            UnityEngine.Physics.SyncTransforms();

            Collider[] results;

            switch (shape.ToLowerInvariant())
            {
                case "sphere":
                {
                    float radius = sizeToken.Value<float>();
                    results = UnityEngine.Physics.OverlapSphere(position, radius, layerMask);
                    break;
                }
                case "box":
                {
                    Vector3 halfExtents;
                    if (sizeToken is JArray sizeArr && sizeArr.Count >= 3)
                        halfExtents = new Vector3(
                            sizeArr[0].Value<float>(), sizeArr[1].Value<float>(), sizeArr[2].Value<float>());
                    else
                        return new ErrorResponse("3D box overlap 'size' requires [halfX, halfY, halfZ].");
                    results = UnityEngine.Physics.OverlapBox(position, halfExtents, Quaternion.identity, layerMask);
                    break;
                }
                case "capsule":
                {
                    if (sizeToken is JObject capsuleObj)
                    {
                        float radius = capsuleObj["radius"]?.Value<float>() ?? 0.5f;
                        float height = capsuleObj["height"]?.Value<float>() ?? 2f;
                        int direction = capsuleObj["direction"]?.Value<int>() ?? 1;

                        Vector3 point0, point1;
                        float halfHeight = Mathf.Max(0, height / 2f - radius);
                        switch (direction)
                        {
                            case 0: // X
                                point0 = position + Vector3.left * halfHeight;
                                point1 = position + Vector3.right * halfHeight;
                                break;
                            case 2: // Z
                                point0 = position + Vector3.back * halfHeight;
                                point1 = position + Vector3.forward * halfHeight;
                                break;
                            default: // Y (1)
                                point0 = position + Vector3.down * halfHeight;
                                point1 = position + Vector3.up * halfHeight;
                                break;
                        }

                        results = UnityEngine.Physics.OverlapCapsule(point0, point1, radius, layerMask);
                    }
                    else
                    {
                        return new ErrorResponse("3D capsule overlap 'size' requires {radius, height, direction}.");
                    }
                    break;
                }
                default:
                    return new ErrorResponse($"Unknown 3D shape: '{shape}'. Valid: sphere, box, capsule.");
            }

            return FormatOverlapResults(results, "3D");
        }

        private static object Overlap2D(string shape, JArray posArr, JToken sizeToken, int layerMask)
        {
            if (posArr.Count < 2)
                return new ErrorResponse("2D overlap 'position' requires [x, y].");

            var position = new Vector2(posArr[0].Value<float>(), posArr[1].Value<float>());

            Physics2D.SyncTransforms();

            Collider2D[] results;

            switch (shape.ToLowerInvariant())
            {
                case "circle":
                {
                    float radius = sizeToken.Value<float>();
                    results = Physics2D.OverlapCircleAll(position, radius, layerMask);
                    break;
                }
                case "box":
                {
                    Vector2 size;
                    if (sizeToken is JArray sizeArr && sizeArr.Count >= 2)
                        size = new Vector2(sizeArr[0].Value<float>(), sizeArr[1].Value<float>());
                    else
                        return new ErrorResponse("2D box overlap 'size' requires [width, height].");
                    results = Physics2D.OverlapBoxAll(position, size, 0f, layerMask);
                    break;
                }
                case "capsule":
                {
                    if (sizeToken is JObject capsuleObj)
                    {
                        float sizeX = capsuleObj["width"]?.Value<float>() ?? 1f;
                        float sizeY = capsuleObj["height"]?.Value<float>() ?? 2f;
                        var dir = CapsuleDirection2D.Vertical;
                        string dirStr = capsuleObj["direction"]?.ToString();
                        if (dirStr != null && dirStr.ToLowerInvariant() == "horizontal")
                            dir = CapsuleDirection2D.Horizontal;
                        results = Physics2D.OverlapCapsuleAll(position, new Vector2(sizeX, sizeY), dir, 0f, layerMask);
                    }
                    else
                    {
                        return new ErrorResponse("2D capsule overlap 'size' requires {width, height, direction}.");
                    }
                    break;
                }
                default:
                    return new ErrorResponse($"Unknown 2D shape: '{shape}'. Valid: circle, box, capsule.");
            }

            return FormatOverlapResults2D(results, "2D");
        }

        private static object FormatOverlapResults(Collider[] results, string dimension)
        {
            var colliders = new List<object>();
            foreach (var col in results)
            {
                colliders.Add(new
                {
                    gameObject = col.gameObject.name,
                    instanceID = col.gameObject.GetInstanceID(),
                    collider_type = col.GetType().Name
                });
            }

            return new
            {
                success = true,
                message = $"Overlap query found {colliders.Count} collider(s) ({dimension}).",
                data = new { colliders }
            };
        }

        private static object FormatOverlapResults2D(Collider2D[] results, string dimension)
        {
            var colliders = new List<object>();
            foreach (var col in results)
            {
                colliders.Add(new
                {
                    gameObject = col.gameObject.name,
                    instanceID = col.gameObject.GetInstanceID(),
                    collider_type = col.GetType().Name
                });
            }

            return new
            {
                success = true,
                message = $"Overlap query found {colliders.Count} collider(s) ({dimension}).",
                data = new { colliders }
            };
        }

        public static object Shapecast(JObject @params)
        {
            var p = new ToolParams(@params);
            string dimension = (p.Get("dimension") ?? "3d").ToLowerInvariant();
            string shape = p.Get("shape");
            if (string.IsNullOrEmpty(shape))
                return new ErrorResponse("'shape' parameter is required (sphere, box, capsule for 3D; circle, box, capsule for 2D).");

            var originArr = p.GetRaw("origin") as JArray;
            if (originArr == null)
                return new ErrorResponse("'origin' parameter is required (array of floats).");

            var dirArr = p.GetRaw("direction") as JArray;
            if (dirArr == null)
                return new ErrorResponse("'direction' parameter is required (array of floats).");

            float maxDistance = p.GetFloat("max_distance") ?? Mathf.Infinity;

            if (dimension == "2d")
                return Shapecast2D(shape, originArr, dirArr, maxDistance, p);

            if (dimension != "3d")
                return new ErrorResponse($"Invalid dimension: '{dimension}'. Use '3d' or '2d'.");

            return Shapecast3D(shape, originArr, dirArr, maxDistance, p);
        }

        private static object Shapecast3D(string shape, JArray originArr, JArray dirArr, float maxDistance, ToolParams p)
        {
            if (originArr.Count < 3)
                return new ErrorResponse("3D shapecast 'origin' requires [x, y, z].");
            if (dirArr.Count < 3)
                return new ErrorResponse("3D shapecast 'direction' requires [x, y, z].");

            var origin = new Vector3(
                originArr[0].Value<float>(), originArr[1].Value<float>(), originArr[2].Value<float>());
            var direction = new Vector3(
                dirArr[0].Value<float>(), dirArr[1].Value<float>(), dirArr[2].Value<float>());

            int layerMask = ResolveLayerMask(p.Get("layer_mask"));

            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal;
            string qti = p.Get("query_trigger_interaction");
            if (!string.IsNullOrEmpty(qti))
            {
                if (!System.Enum.TryParse(qti, true, out triggerInteraction))
                    return new ErrorResponse($"Invalid query_trigger_interaction: '{qti}'. Valid: UseGlobal, Ignore, Collide.");
            }

            var sizeToken = p.GetRaw("size");
            if (sizeToken == null)
                return new ErrorResponse("'size' parameter is required.");

            UnityEngine.Physics.SyncTransforms();

            bool hit;
            RaycastHit hitInfo;

            switch (shape.ToLowerInvariant())
            {
                case "sphere":
                {
                    float radius = sizeToken.Value<float>();
                    hit = UnityEngine.Physics.SphereCast(origin, radius, direction, out hitInfo, maxDistance, layerMask, triggerInteraction);
                    break;
                }
                case "box":
                {
                    Vector3 halfExtents;
                    if (sizeToken is JArray sizeArr && sizeArr.Count >= 3)
                        halfExtents = new Vector3(
                            sizeArr[0].Value<float>(), sizeArr[1].Value<float>(), sizeArr[2].Value<float>());
                    else
                        return new ErrorResponse("3D box shapecast 'size' requires [halfX, halfY, halfZ].");
                    hit = UnityEngine.Physics.BoxCast(origin, halfExtents, direction, out hitInfo, Quaternion.identity, maxDistance, layerMask, triggerInteraction);
                    break;
                }
                case "capsule":
                {
                    float radius = sizeToken.Value<float>();
                    Vector3 pt1, pt2;

                    var pt1Arr = p.GetRaw("point1") as JArray;
                    var pt2Arr = p.GetRaw("point2") as JArray;

                    if (pt1Arr != null && pt1Arr.Count >= 3 && pt2Arr != null && pt2Arr.Count >= 3)
                    {
                        pt1 = new Vector3(pt1Arr[0].Value<float>(), pt1Arr[1].Value<float>(), pt1Arr[2].Value<float>());
                        pt2 = new Vector3(pt2Arr[0].Value<float>(), pt2Arr[1].Value<float>(), pt2Arr[2].Value<float>());
                    }
                    else
                    {
                        float height = p.GetFloat("height") ?? 2f;
                        int capsuleDirection = p.GetInt("capsule_direction") ?? 1;
                        float halfHeight = Mathf.Max(0, height / 2f - radius);

                        switch (capsuleDirection)
                        {
                            case 0: // X
                                pt1 = origin + Vector3.left * halfHeight;
                                pt2 = origin + Vector3.right * halfHeight;
                                break;
                            case 2: // Z
                                pt1 = origin + Vector3.back * halfHeight;
                                pt2 = origin + Vector3.forward * halfHeight;
                                break;
                            default: // Y (1)
                                pt1 = origin + Vector3.down * halfHeight;
                                pt2 = origin + Vector3.up * halfHeight;
                                break;
                        }
                    }

                    hit = UnityEngine.Physics.CapsuleCast(pt1, pt2, radius, direction, out hitInfo, maxDistance, layerMask, triggerInteraction);
                    break;
                }
                default:
                    return new ErrorResponse($"Unknown 3D shape: '{shape}'. Valid: sphere, box, capsule.");
            }

            if (!hit)
            {
                return new
                {
                    success = true,
                    message = "Shapecast did not hit anything.",
                    data = new { hit = false }
                };
            }

            return new
            {
                success = true,
                message = $"Shapecast hit '{hitInfo.collider.gameObject.name}'.",
                data = new
                {
                    hit = true,
                    point = new[] { hitInfo.point.x, hitInfo.point.y, hitInfo.point.z },
                    normal = new[] { hitInfo.normal.x, hitInfo.normal.y, hitInfo.normal.z },
                    distance = hitInfo.distance,
                    gameObject = hitInfo.collider.gameObject.name,
                    instanceID = hitInfo.collider.gameObject.GetInstanceID(),
                    collider_type = hitInfo.collider.GetType().Name
                }
            };
        }

        private static object Shapecast2D(string shape, JArray originArr, JArray dirArr, float maxDistance, ToolParams p)
        {
            if (originArr.Count < 2)
                return new ErrorResponse("2D shapecast 'origin' requires [x, y].");
            if (dirArr.Count < 2)
                return new ErrorResponse("2D shapecast 'direction' requires [x, y].");

            var origin = new Vector2(originArr[0].Value<float>(), originArr[1].Value<float>());
            var direction = new Vector2(dirArr[0].Value<float>(), dirArr[1].Value<float>());

            int layerMask = ResolveLayerMask(p.Get("layer_mask"));
            float angle = p.GetFloat("angle") ?? 0f;

            var sizeToken = p.GetRaw("size");
            if (sizeToken == null)
                return new ErrorResponse("'size' parameter is required.");

            Physics2D.SyncTransforms();

            RaycastHit2D hit;

            switch (shape.ToLowerInvariant())
            {
                case "circle":
                {
                    float radius = sizeToken.Value<float>();
                    hit = Physics2D.CircleCast(origin, radius, direction, maxDistance, layerMask);
                    break;
                }
                case "box":
                {
                    Vector2 size;
                    if (sizeToken is JArray sizeArr && sizeArr.Count >= 2)
                        size = new Vector2(sizeArr[0].Value<float>(), sizeArr[1].Value<float>());
                    else
                        return new ErrorResponse("2D box shapecast 'size' requires [width, height].");
                    hit = Physics2D.BoxCast(origin, size, angle, direction, maxDistance, layerMask);
                    break;
                }
                case "capsule":
                {
                    if (sizeToken is JObject sizeObj)
                    {
                        var capsuleSize = new Vector2(
                            sizeObj["width"]?.Value<float>() ?? 1f,
                            sizeObj["height"]?.Value<float>() ?? 2f);
                        var capsuleDir = CapsuleDirection2D.Vertical;
                        string dirStr = sizeObj["direction"]?.ToString();
                        if (dirStr != null && dirStr.ToLowerInvariant() == "horizontal")
                            capsuleDir = CapsuleDirection2D.Horizontal;
                        hit = Physics2D.CapsuleCast(origin, capsuleSize, capsuleDir, angle, direction, maxDistance, layerMask);
                    }
                    else
                    {
                        return new ErrorResponse("2D capsule shapecast 'size' requires {width, height, direction}.");
                    }
                    break;
                }
                default:
                    return new ErrorResponse($"Unknown 2D shape: '{shape}'. Valid: circle, box, capsule.");
            }

            if (hit.collider == null)
            {
                return new
                {
                    success = true,
                    message = "2D Shapecast did not hit anything.",
                    data = new { hit = false }
                };
            }

            return new
            {
                success = true,
                message = $"2D Shapecast hit '{hit.collider.gameObject.name}'.",
                data = new
                {
                    hit = true,
                    point = new[] { hit.point.x, hit.point.y },
                    normal = new[] { hit.normal.x, hit.normal.y },
                    distance = hit.distance,
                    gameObject = hit.collider.gameObject.name,
                    instanceID = hit.collider.gameObject.GetInstanceID(),
                    collider_type = hit.collider.GetType().Name
                }
            };
        }

        public static object RaycastAll(JObject @params)
        {
            var p = new ToolParams(@params);
            string dimension = (p.Get("dimension") ?? "3d").ToLowerInvariant();

            var originArr = p.GetRaw("origin") as JArray;
            if (originArr == null)
                return new ErrorResponse("'origin' parameter is required (array of floats).");

            var dirArr = p.GetRaw("direction") as JArray;
            if (dirArr == null)
                return new ErrorResponse("'direction' parameter is required (array of floats).");

            float maxDistance = p.GetFloat("max_distance") ?? Mathf.Infinity;

            if (dimension == "2d")
                return RaycastAll2D(originArr, dirArr, maxDistance, p);

            if (dimension != "3d")
                return new ErrorResponse($"Invalid dimension: '{dimension}'. Use '3d' or '2d'.");

            return RaycastAll3D(originArr, dirArr, maxDistance, p);
        }

        private static object RaycastAll3D(JArray originArr, JArray dirArr, float maxDistance, ToolParams p)
        {
            if (originArr.Count < 3)
                return new ErrorResponse("3D RaycastAll 'origin' requires [x, y, z].");
            if (dirArr.Count < 3)
                return new ErrorResponse("3D RaycastAll 'direction' requires [x, y, z].");

            var origin = new Vector3(
                originArr[0].Value<float>(), originArr[1].Value<float>(), originArr[2].Value<float>());
            var direction = new Vector3(
                dirArr[0].Value<float>(), dirArr[1].Value<float>(), dirArr[2].Value<float>());

            int layerMask = ResolveLayerMask(p.Get("layer_mask"));

            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal;
            string qti = p.Get("query_trigger_interaction");
            if (!string.IsNullOrEmpty(qti))
            {
                if (!System.Enum.TryParse(qti, true, out triggerInteraction))
                    return new ErrorResponse($"Invalid query_trigger_interaction: '{qti}'. Valid: UseGlobal, Ignore, Collide.");
            }

            UnityEngine.Physics.SyncTransforms();

            RaycastHit[] hits = UnityEngine.Physics.RaycastAll(origin, direction, maxDistance, layerMask, triggerInteraction);
            System.Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));

            var hitsArray = new List<object>();
            foreach (var h in hits)
            {
                hitsArray.Add(new
                {
                    point = new[] { h.point.x, h.point.y, h.point.z },
                    normal = new[] { h.normal.x, h.normal.y, h.normal.z },
                    distance = h.distance,
                    gameObject = h.collider.gameObject.name,
                    instanceID = h.collider.gameObject.GetInstanceID(),
                    collider_type = h.collider.GetType().Name
                });
            }

            return new
            {
                success = true,
                message = $"RaycastAll found {hits.Length} hit(s).",
                data = new { hit_count = hits.Length, hits = hitsArray }
            };
        }

        private static object RaycastAll2D(JArray originArr, JArray dirArr, float maxDistance, ToolParams p)
        {
            if (originArr.Count < 2)
                return new ErrorResponse("2D RaycastAll 'origin' requires [x, y].");
            if (dirArr.Count < 2)
                return new ErrorResponse("2D RaycastAll 'direction' requires [x, y].");

            var origin = new Vector2(originArr[0].Value<float>(), originArr[1].Value<float>());
            var direction = new Vector2(dirArr[0].Value<float>(), dirArr[1].Value<float>());

            int layerMask = ResolveLayerMask(p.Get("layer_mask"));

            Physics2D.SyncTransforms();

            RaycastHit2D[] hits = Physics2D.RaycastAll(origin, direction, maxDistance, layerMask);

            var hitsArray = new List<object>();
            foreach (var h in hits)
            {
                hitsArray.Add(new
                {
                    point = new[] { h.point.x, h.point.y },
                    normal = new[] { h.normal.x, h.normal.y },
                    distance = h.distance,
                    gameObject = h.collider.gameObject.name,
                    instanceID = h.collider.gameObject.GetInstanceID(),
                    collider_type = h.collider.GetType().Name
                });
            }

            return new
            {
                success = true,
                message = $"RaycastAll found {hits.Length} hit(s).",
                data = new { hit_count = hits.Length, hits = hitsArray }
            };
        }

        public static object Linecast(JObject @params)
        {
            var p = new ToolParams(@params);
            string dimension = (p.Get("dimension") ?? "3d").ToLowerInvariant();

            var startArr = p.GetRaw("start") as JArray;
            if (startArr == null)
                return new ErrorResponse("'start' parameter is required (array of floats).");

            var endArr = p.GetRaw("end") as JArray;
            if (endArr == null)
                return new ErrorResponse("'end' parameter is required (array of floats).");

            if (dimension == "2d")
                return Linecast2D(startArr, endArr, p);

            if (dimension != "3d")
                return new ErrorResponse($"Invalid dimension: '{dimension}'. Use '3d' or '2d'.");

            return Linecast3D(startArr, endArr, p);
        }

        private static object Linecast3D(JArray startArr, JArray endArr, ToolParams p)
        {
            if (startArr.Count < 3)
                return new ErrorResponse("3D linecast 'start' requires [x, y, z].");
            if (endArr.Count < 3)
                return new ErrorResponse("3D linecast 'end' requires [x, y, z].");

            var start = new Vector3(
                startArr[0].Value<float>(), startArr[1].Value<float>(), startArr[2].Value<float>());
            var end = new Vector3(
                endArr[0].Value<float>(), endArr[1].Value<float>(), endArr[2].Value<float>());

            int layerMask = ResolveLayerMask(p.Get("layer_mask"));

            QueryTriggerInteraction triggerInteraction = QueryTriggerInteraction.UseGlobal;
            string qti = p.Get("query_trigger_interaction");
            if (!string.IsNullOrEmpty(qti))
            {
                if (!System.Enum.TryParse(qti, true, out triggerInteraction))
                    return new ErrorResponse($"Invalid query_trigger_interaction: '{qti}'. Valid: UseGlobal, Ignore, Collide.");
            }

            UnityEngine.Physics.SyncTransforms();

            bool hit = UnityEngine.Physics.Linecast(start, end, out RaycastHit hitInfo, layerMask, triggerInteraction);

            if (!hit)
            {
                return new
                {
                    success = true,
                    message = "Linecast did not hit anything.",
                    data = new { hit = false }
                };
            }

            return new
            {
                success = true,
                message = $"Linecast hit '{hitInfo.collider.gameObject.name}'.",
                data = new
                {
                    hit = true,
                    point = new[] { hitInfo.point.x, hitInfo.point.y, hitInfo.point.z },
                    normal = new[] { hitInfo.normal.x, hitInfo.normal.y, hitInfo.normal.z },
                    distance = hitInfo.distance,
                    gameObject = hitInfo.collider.gameObject.name,
                    instanceID = hitInfo.collider.gameObject.GetInstanceID(),
                    collider_type = hitInfo.collider.GetType().Name
                }
            };
        }

        private static object Linecast2D(JArray startArr, JArray endArr, ToolParams p)
        {
            if (startArr.Count < 2)
                return new ErrorResponse("2D linecast 'start' requires [x, y].");
            if (endArr.Count < 2)
                return new ErrorResponse("2D linecast 'end' requires [x, y].");

            var start = new Vector2(startArr[0].Value<float>(), startArr[1].Value<float>());
            var end = new Vector2(endArr[0].Value<float>(), endArr[1].Value<float>());

            int layerMask = ResolveLayerMask(p.Get("layer_mask"));

            Physics2D.SyncTransforms();

            var hit = Physics2D.Linecast(start, end, layerMask);

            if (hit.collider == null)
            {
                return new
                {
                    success = true,
                    message = "2D Linecast did not hit anything.",
                    data = new { hit = false }
                };
            }

            return new
            {
                success = true,
                message = $"2D Linecast hit '{hit.collider.gameObject.name}'.",
                data = new
                {
                    hit = true,
                    point = new[] { hit.point.x, hit.point.y },
                    normal = new[] { hit.normal.x, hit.normal.y },
                    distance = hit.distance,
                    gameObject = hit.collider.gameObject.name,
                    instanceID = hit.collider.gameObject.GetInstanceID(),
                    collider_type = hit.collider.GetType().Name
                }
            };
        }

        private static int ResolveLayerMask(string layerMaskStr)
        {
            if (string.IsNullOrEmpty(layerMaskStr))
                return ~0; // All layers

            if (int.TryParse(layerMaskStr, out int mask))
                return mask;

            int layer = LayerMask.NameToLayer(layerMaskStr);
            if (layer >= 0)
                return 1 << layer;

            throw new ArgumentException($"Unknown layer name: '{layerMaskStr}'. Use a valid layer name or integer mask.");
        }
    }
}
