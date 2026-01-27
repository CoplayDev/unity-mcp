#nullable disable
using System.Collections.Generic;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.GameObjects
{
    internal static class GameObjectDelete
    {
        internal static object Handle(JToken targetToken, string searchMethod)
        {
            List<GameObject> targets = ManageGameObjectCommon.FindObjectsInternal(targetToken, searchMethod, true);

            if (targets.Count == 0)
            {
                return new ErrorResponse($"Target GameObject(s) ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            List<object> deletedObjects = new List<object>();
            foreach (var targetGo in targets)
            {
                if (targetGo != null)
                {
                    string goName = targetGo.name;
                    int goId = targetGo.GetInstanceID();
                    // Note: Undo.DestroyObjectImmediate doesn't work reliably in test context,
                    // so we use Object.DestroyImmediate. This means delete isn't undoable.
                    // TODO: Investigate Undo.DestroyObjectImmediate behavior in Unity 2022+
                    Object.DestroyImmediate(targetGo);
                    deletedObjects.Add(new { name = goName, instanceID = goId });
                }
            }

            if (deletedObjects.Count > 0)
            {
                string message =
                    targets.Count == 1
                        ? $"GameObject '{((dynamic)deletedObjects[0]).name}' deleted successfully."
                        : $"{deletedObjects.Count} GameObjects deleted successfully.";
                return new SuccessResponse(message, deletedObjects);
            }

            return new ErrorResponse("Failed to delete target GameObject(s).");
        }
    }
}
