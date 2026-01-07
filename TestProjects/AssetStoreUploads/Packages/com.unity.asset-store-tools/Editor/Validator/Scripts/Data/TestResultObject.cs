using Newtonsoft.Json;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.Data
{
    internal class TestResultObject
    {
        [JsonIgnore]
        private Object _object;
        [JsonProperty]
        private string _objectGlobalId;

        public TestResultObject(Object obj)
        {
            _object = obj;
            _objectGlobalId = GlobalObjectId.GetGlobalObjectIdSlow(obj).ToString();
        }

        public Object GetObject()
        {
            if (_object != null)
                return _object;

            if (string.IsNullOrEmpty(_objectGlobalId))
                return null;

            if (!GlobalObjectId.TryParse(_objectGlobalId, out var globalObject))
                return null;

            _object = GlobalObjectId.GlobalObjectIdentifierToObjectSlow(globalObject);
            return _object;
        }
    }
}