using System;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Api
{
    internal class AssetStoreClient : IAssetStoreClient
    {
        private HttpClient _httpClient;

        public AssetStoreClient()
        {
            ServicePointManager.DefaultConnectionLimit = 500;
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.ConnectionClose = false;
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _httpClient.Timeout = TimeSpan.FromMinutes(1320);
        }

        public void SetSessionId(string sessionId)
        {
            ClearSessionId();

            if (!string.IsNullOrEmpty(sessionId))
                _httpClient.DefaultRequestHeaders.Add("X-Unity-Session", sessionId);
        }

        public void ClearSessionId()
        {
            _httpClient.DefaultRequestHeaders.Remove("X-Unity-Session");
        }

        public Task<HttpResponseMessage> Get(Uri uri, CancellationToken cancellationToken = default)
        {
            return _httpClient.GetAsync(uri, cancellationToken);
        }

        public Task<HttpResponseMessage> Post(Uri uri, HttpContent content, CancellationToken cancellationToken = default)
        {
            return _httpClient.PostAsync(uri, content, cancellationToken);
        }

        public Task<HttpResponseMessage> Put(Uri uri, HttpContent content, CancellationToken cancellationToken = default)
        {
            return _httpClient.PutAsync(uri, content, cancellationToken);
        }

        public Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken = default)
        {
            return _httpClient.SendAsync(request, cancellationToken);
        }
    }
}
