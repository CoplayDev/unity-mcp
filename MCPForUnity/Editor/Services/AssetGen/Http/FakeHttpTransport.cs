using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace MCPForUnity.Editor.Services.AssetGen.Http
{
    /// <summary>
    /// Test double for <see cref="IHttpTransport"/>. Records every request it is handed and
    /// returns a canned response, so provider adapters can be exercised without a network.
    /// Match responses either with the <see cref="Handler"/> delegate (full control) or with
    /// <see cref="ByUrlSubstring"/> (first entry whose key is contained in the request URL).
    /// </summary>
    public sealed class FakeHttpTransport : IHttpTransport
    {
        public List<HttpRequestSpec> RecordedRequests { get; } = new List<HttpRequestSpec>();

        /// <summary>Highest-priority responder; return null to fall through to <see cref="ByUrlSubstring"/>.</summary>
        public Func<HttpRequestSpec, HttpResult> Handler { get; set; }

        /// <summary>Canned responses keyed by a substring expected to appear in the request URL.</summary>
        public Dictionary<string, HttpResult> ByUrlSubstring { get; } = new Dictionary<string, HttpResult>();

        public Task<HttpResult> SendAsync(HttpRequestSpec spec, CancellationToken ct)
        {
            RecordedRequests.Add(spec);

            HttpResult result = Handler?.Invoke(spec);

            if (result == null && spec?.Url != null)
            {
                foreach (var kv in ByUrlSubstring)
                {
                    if (spec.Url.IndexOf(kv.Key, StringComparison.Ordinal) >= 0)
                    {
                        result = kv.Value;
                        break;
                    }
                }
            }

            if (result == null)
            {
                result = new HttpResult
                {
                    Status = 500,
                    IsSuccess = false,
                    Text = "FakeHttpTransport: no canned response matched " + (spec?.Url ?? "<null>")
                };
            }

            return Task.FromResult(result);
        }
    }
}
