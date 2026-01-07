#nullable disable
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Tools.GameObjects
{
    internal static class GameObjectHandlers
    {
        internal static object Create(JObject @params) => GameObjectCreate.Handle(@params);

        internal static object Modify(JObject @params, JToken targetToken, string searchMethod)
            => GameObjectModify.Handle(@params, targetToken, searchMethod);

        internal static object Delete(JToken targetToken, string searchMethod)
            => GameObjectDelete.Handle(targetToken, searchMethod);

        internal static object Duplicate(JObject @params, JToken targetToken, string searchMethod)
            => GameObjectDuplicate.Handle(@params, targetToken, searchMethod);

        internal static object MoveRelative(JObject @params, JToken targetToken, string searchMethod)
            => GameObjectMoveRelative.Handle(@params, targetToken, searchMethod);
    }
}
