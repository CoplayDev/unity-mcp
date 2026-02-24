#nullable disable
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.GameObjects
{
    internal static class GameObjectLookAt
    {
        /// <summary>
        /// Rotates a GameObject to face a world position or another GameObject.
        /// Parameters:
        ///   target       - The GO to rotate (name/path/instanceID)
        ///   look_at_target - World position [x,y,z] or GO reference (name/path/instanceID) to look at
        ///   look_at_up   - Optional up vector [x,y,z], defaults to Vector3.up
        /// </summary>
        internal static object Handle(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject targetGo = ManageGameObjectCommon.FindObjectInternal(targetToken, searchMethod);
            if (targetGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            JToken lookAtToken = @params["look_at_target"] ?? @params["lookAtTarget"];
            if (lookAtToken == null)
            {
                return new ErrorResponse("'look_at_target' parameter is required for 'look_at' action. Provide a world position [x,y,z] or a GameObject name/path/ID.");
            }

            // Try parsing as a position vector first
            Vector3? lookAtPos = VectorParsing.ParseVector3(lookAtToken);
            if (!lookAtPos.HasValue)
            {
                // Not a vector — treat as a GO reference, using the same search method as for the main target
                GameObject lookAtGo = ManageGameObjectCommon.FindObjectInternal(lookAtToken, searchMethod);
                if (lookAtGo == null)
                {
                    return new ErrorResponse($"look_at_target '{lookAtToken}' could not be resolved as a position [x,y,z] or found as a GameObject.");
                }
                lookAtPos = lookAtGo.transform.position;
            }

            Vector3 upVector = VectorParsing.ParseVector3OrDefault(@params["look_at_up"] ?? @params["lookAtUp"], Vector3.up);

            Undo.RecordObject(targetGo.transform, $"LookAt {targetGo.name}");
            targetGo.transform.LookAt(lookAtPos.Value, upVector);

            var euler = targetGo.transform.rotation.eulerAngles;
            return new SuccessResponse(
                $"'{targetGo.name}' now looking at ({lookAtPos.Value.x:F2}, {lookAtPos.Value.y:F2}, {lookAtPos.Value.z:F2}).",
                new
                {
                    name = targetGo.name,
                    instanceID = targetGo.GetInstanceID(),
                    rotation = new[] { euler.x, euler.y, euler.z },
                    lookAtPosition = new[] { lookAtPos.Value.x, lookAtPos.Value.y, lookAtPos.Value.z },
                }
            );
        }
    }
}
