using Newtonsoft.Json;
using UnityEngine;

namespace AssetStoreTools.Validator.Data.MessageActions
{
    internal interface IMessageAction
    {
        [JsonIgnore]
        string Tooltip { get; }

        [JsonIgnore]
        Object Target { get; }

        void Execute();
    }
}