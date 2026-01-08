#nullable disable
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace MCPForUnity.Editor.Tools.GameObjects
{
    internal static class GameObjectDuplicate
    {
        internal static object Handle(JObject @params, JToken targetToken, string searchMethod)
        {
            GameObject sourceGo = ManageGameObjectCommon.FindObjectInternal(targetToken, searchMethod);
            if (sourceGo == null)
            {
                return new ErrorResponse($"Target GameObject ('{targetToken}') not found using method '{searchMethod ?? "default"}'.");
            }

            string newName = @params["new_name"]?.ToString();
            Vector3? position = VectorParsing.ParseVector3(@params["position"]);
            Vector3? offset = VectorParsing.ParseVector3(@params["offset"]);
            JToken parentToken = @params["parent"];

            GameObject duplicatedGo = UnityEngine.Object.Instantiate(sourceGo);
            Undo.RegisterCreatedObjectUndo(duplicatedGo, $"Duplicate {sourceGo.name}");

            if (!string.IsNullOrEmpty(newName))
            {
                duplicatedGo.name = newName;
            }
            else
            {
                duplicatedGo.name = sourceGo.name.Replace("(Clone)", "").Trim() + "_Copy";
            }

            if (position.HasValue)
            {
                duplicatedGo.transform.position = position.Value;
            }
            else if (offset.HasValue)
            {
                duplicatedGo.transform.position = sourceGo.transform.position + offset.Value;
            }

            if (parentToken != null)
            {
                if (parentToken.Type == JTokenType.Null || (parentToken.Type == JTokenType.String && string.IsNullOrEmpty(parentToken.ToString())))
                {
                    duplicatedGo.transform.SetParent(null);
                }
                else
                {
                    GameObject newParent = ManageGameObjectCommon.FindObjectInternal(parentToken, "by_id_or_name_or_path");
                    if (newParent != null)
                    {
                        duplicatedGo.transform.SetParent(newParent.transform, true);
                    }
                    else
                    {
                        McpLog.Warn($"[ManageGameObject.Duplicate] Parent '{parentToken}' not found. Object will remain at root level.");
                    }
                }
            }
            else
            {
                duplicatedGo.transform.SetParent(sourceGo.transform.parent, true);
            }

            EditorUtility.SetDirty(duplicatedGo);
            EditorSceneManager.MarkSceneDirty(EditorSceneManager.GetActiveScene());

            Selection.activeGameObject = duplicatedGo;

            return new SuccessResponse(
                $"Duplicated '{sourceGo.name}' as '{duplicatedGo.name}'.",
                new
                {
                    originalName = sourceGo.name,
                    originalId = sourceGo.GetInstanceID(),
                    duplicatedObject = Helpers.GameObjectSerializer.GetGameObjectData(duplicatedGo)
                }
            );
        }
    }
}
