using AssetStoreTools.Validator.Data.MessageActions;
using Newtonsoft.Json;
using System.Collections.Generic;

namespace AssetStoreTools.Validator.Data
{
    internal struct TestResult
    {
        public TestResultStatus Status;

        [JsonProperty]
        private List<TestResultMessage> _messages;

        [JsonIgnore]
        public int MessageCount => _messages != null ? _messages.Count : 0;

        public TestResultMessage GetMessage(int index)
        {
            return _messages[index];
        }

        public void AddMessage(string msg)
        {
            AddMessage(msg, null, null);
        }

        public void AddMessage(string msg, IMessageAction clickAction)
        {
            AddMessage(msg, clickAction, null);
        }

        public void AddMessage(string msg, IMessageAction clickAction, params UnityEngine.Object[] messageObjects)
        {
            if (_messages == null)
                _messages = new List<TestResultMessage>();

            var message = new TestResultMessage(msg, clickAction);
            _messages.Add(message);

            if (messageObjects == null)
                return;

            foreach (var obj in messageObjects)
            {
                if (obj == null)
                    continue;

                message.AddMessageObject(obj);
            }
        }
    }
}