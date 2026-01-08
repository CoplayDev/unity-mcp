using AssetStoreTools.Validator.Data.MessageActions;
using Newtonsoft.Json;
using System.Collections.Generic;
using Object = UnityEngine.Object;

namespace AssetStoreTools.Validator.Data
{
    internal class TestResultMessage
    {
        [JsonIgnore]
        public int MessageObjectCount => _messageObjects.Count;

        [JsonProperty]
        private string _text;
        [JsonProperty]
        private List<TestResultObject> _messageObjects;
        [JsonProperty]
        private IMessageAction _clickAction;

        public TestResultMessage() { }

        public TestResultMessage(string text)
        {
            _text = text;
            _messageObjects = new List<TestResultObject>();
        }

        public TestResultMessage(string text, IMessageAction clickAction) : this(text)
        {
            _clickAction = clickAction;
        }

        public string GetText()
        {
            return _text;
        }

        public IMessageAction GetClickAction()
        {
            return _clickAction;
        }

        public void AddMessageObject(Object obj)
        {
            _messageObjects.Add(new TestResultObject(obj));
        }

        public TestResultObject GetMessageObject(int index)
        {
            return _messageObjects[index];
        }
    }
}