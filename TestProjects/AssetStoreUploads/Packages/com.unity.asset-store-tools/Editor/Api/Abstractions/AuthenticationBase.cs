using AssetStoreTools.Api.Responses;
using AssetStoreTools.Utility;
using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetStoreTools.Api
{
    internal abstract class AuthenticationBase : IAuthenticationType
    {
        protected Uri LoginUrl = ApiUtility.CreateUri(Constants.Api.AuthenticateUrl, true);
        protected FormUrlEncodedContent AuthenticationContent;

        protected FormUrlEncodedContent GetAuthenticationContent(params KeyValuePair<string, string>[] content)
        {
            var baseContent = Constants.Api.DefaultAssetStoreQuery();

            try { baseContent.Add("license_hash", ApiUtility.GetLicenseHash()); } catch { ASDebug.LogWarning("Could not retrieve license hash"); }
            try { baseContent.Add("hardware_hash", ApiUtility.GetHardwareHash()); } catch { ASDebug.LogWarning("Could not retrieve hardware hash"); }

            foreach (var extraContent in content)
            {
                baseContent.Add(extraContent.Key, extraContent.Value);
            }

            return new FormUrlEncodedContent(baseContent);
        }

        protected AuthenticationResponse ParseResponse(HttpResponseMessage response)
        {
            try
            {
                response.EnsureSuccessStatusCode();
                var responseString = response.Content.ReadAsStringAsync().Result;
                return new AuthenticationResponse(responseString);
            }
            catch (HttpRequestException e)
            {
                return new AuthenticationResponse(response.StatusCode, e) { Success = false };
            }
        }

        public abstract Task<AuthenticationResponse> Authenticate(IAssetStoreClient client, CancellationToken cancellationToken = default);
    }
}