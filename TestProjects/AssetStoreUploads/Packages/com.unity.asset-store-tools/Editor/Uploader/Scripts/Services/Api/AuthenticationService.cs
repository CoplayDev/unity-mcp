using AssetStoreTools.Api;
using AssetStoreTools.Api.Models;
using AssetStoreTools.Api.Responses;
using AssetStoreTools.Uploader.Services.Analytics;
using AssetStoreTools.Uploader.Services.Analytics.Data;
using AssetStoreTools.Utility;
using System;
using System.Threading.Tasks;
using UnityEditor;

namespace AssetStoreTools.Uploader.Services.Api
{
    internal class AuthenticationService : IAuthenticationService
    {
        private IAssetStoreApi _api;
        private ICachingService _cachingService;
        private IAnalyticsService _analyticsService;

        public User User { get; private set; }

        public AuthenticationService(IAssetStoreApi api, ICachingService cachingService, IAnalyticsService analyticsService)
        {
            _api = api;
            _cachingService = cachingService;
            _analyticsService = analyticsService;
        }

        public async Task<AuthenticationResponse> AuthenticateWithCredentials(string email, string password)
        {
            var authenticationType = new CredentialsAuthentication(email, password);
            return await Authenticate(authenticationType);
        }

        public async Task<AuthenticationResponse> AuthenticateWithSessionToken()
        {
            if (!_cachingService.GetCachedSessionToken(out var cachedSessionToken))
            {
                return new AuthenticationResponse() { Success = false, Exception = new Exception("No cached session token found") };
            }

            var authenticationType = new SessionAuthentication(cachedSessionToken);
            return await Authenticate(authenticationType);
        }

        public async Task<AuthenticationResponse> AuthenticateWithCloudToken()
        {
            var authenticationType = new CloudTokenAuthentication(CloudProjectSettings.accessToken);
            return await Authenticate(authenticationType);
        }

        private async Task<AuthenticationResponse> Authenticate(IAuthenticationType authenticationType)
        {
            var response = await _api.Authenticate(authenticationType);
            HandleLoginResponse(authenticationType, response);
            return response;
        }

        private void HandleLoginResponse(IAuthenticationType authenticationType, AuthenticationResponse response)
        {
            if (!response.Success)
            {
                Deauthenticate();
                return;
            }

            User = response.User;
            _cachingService.CacheSessionToken(User.SessionId);
            SendAnalytics(authenticationType, User);
        }

        public bool CloudAuthenticationAvailable(out string username, out string cloudToken)
        {
            username = CloudProjectSettings.userName;
            cloudToken = CloudProjectSettings.accessToken;
            return !username.Equals("anonymous");
        }

        public void Deauthenticate()
        {
            _api.Deauthenticate();

            User = null;
            _cachingService.ClearCachedSessionToken();
        }

        private void SendAnalytics(IAuthenticationType authenticationType, User user)
        {
            try
            {
                // Do not send session authentication events
                if (authenticationType is SessionAuthentication)
                    return;

                var analytic = new AuthenticationAnalytic(authenticationType, user.PublisherId);
                var result = _analyticsService.Send(analytic);
            }
            catch (Exception e) { ASDebug.LogError($"Could not send analytics: {e}"); }
        }
    }
}