using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Api
{
    internal interface IAssetStoreClient
    {
        void SetSessionId(string sessionId);
        void ClearSessionId();

        Task<HttpResponseMessage> Get(Uri uri, CancellationToken cancellationToken = default);
        Task<HttpResponseMessage> Post(Uri uri, HttpContent content, CancellationToken cancellationToken = default);
        Task<HttpResponseMessage> Put(Uri uri, HttpContent content, CancellationToken cancellationToken = default);
        Task<HttpResponseMessage> Send(HttpRequestMessage request, CancellationToken cancellationToken = default);
    }
}