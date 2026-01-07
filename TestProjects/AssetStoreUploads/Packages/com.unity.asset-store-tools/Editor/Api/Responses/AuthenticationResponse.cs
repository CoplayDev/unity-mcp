using AssetStoreTools.Api.Models;
using Newtonsoft.Json;
using System;
using System.Net;
using System.Net.Http;

namespace AssetStoreTools.Api.Responses
{
    internal class AuthenticationResponse : AssetStoreResponse
    {
        public User User { get; set; }

        public AuthenticationResponse() : base() { }

        public AuthenticationResponse(Exception e) : base(e) { }

        public AuthenticationResponse(HttpStatusCode statusCode, HttpRequestException fallbackException)
        {
            string message;
            switch (statusCode)
            {
                case HttpStatusCode.Unauthorized:
                    message = "Incorrect email and/or password. Please try again.";
                    break;
                case HttpStatusCode.InternalServerError:
                    message = "Authentication request failed\nIf you were logging in with your Unity Cloud account, please make sure you are still logged in.\n" +
                        "This might also be caused by too many invalid login attempts - if that is the case, please try again later.";
                    break;
                default:
                    Exception = fallbackException;
                    return;
            }

            Exception = new Exception(message);
        }

        public AuthenticationResponse(string json)
        {
            try
            {
                ValidateAssetStoreResponse(json);
                var serializerSettings = new JsonSerializerSettings()
                {
                    ContractResolver = User.AssetStoreUserResolver.Instance
                };
                User = JsonConvert.DeserializeObject<User>(json, serializerSettings);
                ValidateLoginData();
                ValidatePublisher();
                Success = true;
            }
            catch (Exception e)
            {
                Success = false;
                Exception = e;
            }
        }

        private void ValidateLoginData()
        {
            if (string.IsNullOrEmpty(User.Id)
                || string.IsNullOrEmpty(User.SessionId)
                || string.IsNullOrEmpty(User.Name)
                || string.IsNullOrEmpty(User.Username))
                throw new Exception("Could not parse the necessary publisher information from the response.");
        }

        private void ValidatePublisher()
        {
            if (!User.IsPublisher)
                throw new Exception($"Your Unity ID {User.Name} is not currently connected to a publisher account. " +
                          $"Please create a publisher profile.");
        }
    }
}