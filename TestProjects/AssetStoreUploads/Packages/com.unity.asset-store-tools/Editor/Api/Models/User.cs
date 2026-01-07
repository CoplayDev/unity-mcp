using Newtonsoft.Json.Serialization;
using System.Collections.Generic;

namespace AssetStoreTools.Api.Models
{
    internal class User
    {
        public string Id { get; set; }
        public string SessionId { get; set; }
        public string Name { get; set; }
        public string Username { get; set; }
        public string PublisherId { get; set; }
        public bool IsPublisher => !string.IsNullOrEmpty(PublisherId);

        public override string ToString()
        {
            return
                $"{nameof(Id)}: {Id}\n" +
                $"{nameof(Name)}: {Name}\n" +
                $"{nameof(Username)}: {Username}\n" +
                $"{nameof(PublisherId)}: {PublisherId}\n" +
                $"{nameof(IsPublisher)}: {IsPublisher}\n" +
                $"{nameof(SessionId)}: [HIDDEN]";
        }

        public class AssetStoreUserResolver : DefaultContractResolver
        {
            private static AssetStoreUserResolver _instance;
            public static AssetStoreUserResolver Instance => _instance ?? (_instance = new AssetStoreUserResolver());

            private Dictionary<string, string> _propertyConversions;

            private AssetStoreUserResolver()
            {
                _propertyConversions = new Dictionary<string, string>()
                {
                    { nameof(User.SessionId), "xunitysession" },
                    { nameof(User.PublisherId), "publisher" }
                };
            }

            protected override string ResolvePropertyName(string propertyName)
            {
                if (_propertyConversions.ContainsKey(propertyName))
                    return _propertyConversions[propertyName];

                return base.ResolvePropertyName(propertyName);
            }
        }
    }
}