using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.Data.MessageActions
{
    internal class HighlightObjectAction : IMessageAction
    {
        public string Tooltip => "Click to highlight the associated object in Hierarchy/Project view";
        public Object Target => _target?.GetObject();

        [JsonProperty]
        private TestResultObject _target;

        public HighlightObjectAction() { }

        public HighlightObjectAction(Object target)
        {
            _target = new TestResultObject(target);
        }

        public void Execute()
        {
            var targetObject = _target.GetObject();
            if (targetObject == null)
                return;

            EditorGUIUtility.PingObject(targetObject);
        }
    }
}