using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.Data.MessageActions
{
    internal class OpenAssetAction : IMessageAction
    {
        public string Tooltip => "Click to open the associated asset";
        public Object Target => _target?.GetObject();

        [JsonProperty]
        private TestResultObject _target;
        [JsonProperty]
        private int _lineNumber;

        public OpenAssetAction() { }

        public OpenAssetAction(Object target)
        {
            _target = new TestResultObject(target);
        }

        public OpenAssetAction(Object target, int lineNumber) : this(target)
        {
            _lineNumber = lineNumber;
        }

        public void Execute()
        {
            var targetObject = _target.GetObject();
            if (targetObject == null)
                return;

            AssetDatabase.OpenAsset(targetObject, _lineNumber);
        }
    }
}