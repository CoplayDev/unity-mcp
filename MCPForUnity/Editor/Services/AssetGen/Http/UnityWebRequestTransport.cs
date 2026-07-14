using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine.Networking;

namespace MCPForUnity.Editor.Services.AssetGen.Http
{
    /// <summary>
    /// Production <see cref="IHttpTransport"/> backed by UnityWebRequest. Must be invoked on the
    /// Unity main thread (the asset-gen job manager guarantees this in Phase 3). The send is
    /// awaited via a <see cref="TaskCompletionSource{T}"/> wired to the async op's completed
    /// callback, so the call never blocks the editor loop.
    /// </summary>
    public sealed class UnityWebRequestTransport : IHttpTransport
    {
        public Task<HttpResult> SendAsync(HttpRequestSpec spec, CancellationToken ct)
        {
            if (spec == null) throw new ArgumentNullException(nameof(spec));

            var tcs = new TaskCompletionSource<HttpResult>();

            var request = new UnityWebRequest(spec.Url, spec.Method ?? UnityWebRequest.kHttpVerbGET)
            {
                downloadHandler = new DownloadHandlerBuffer()
            };
            if (spec.Body != null)
            {
                request.uploadHandler = new UploadHandlerRaw(spec.Body);
            }
            if (!string.IsNullOrEmpty(spec.ContentType))
            {
                request.SetRequestHeader("Content-Type", spec.ContentType);
            }
            if (spec.Headers != null)
            {
                foreach (var kv in spec.Headers)
                {
                    request.SetRequestHeader(kv.Key, kv.Value);
                }
            }
            // UnityWebRequest re-sends the Authorization header to a 3xx target by default. Never
            // follow a redirect on an auth-bearing request — the key must not leak to the redirect
            // host. No-auth downloads may still follow.
            if (CarriesAuth(spec)) request.redirectLimit = 0;

            CancellationTokenRegistration ctReg = default;
            if (ct.CanBeCanceled)
            {
                ctReg = ct.Register(() =>
                {
                    try { request.Abort(); } catch { /* ignore */ }
                    tcs.TrySetCanceled();
                });
            }

            var op = request.SendWebRequest();
            op.completed += _ =>
            {
                try
                {
                    var result = new HttpResult
                    {
                        Status = (int)request.responseCode,
                        Body = request.downloadHandler?.data,
                        Text = request.downloadHandler?.text,
                        IsSuccess = request.result == UnityWebRequest.Result.Success
                    };
                    tcs.TrySetResult(result);
                }
                catch (Exception e)
                {
                    tcs.TrySetException(e);
                }
                finally
                {
                    ctReg.Dispose();
                    request.Dispose();
                }
            };

            return tcs.Task;
        }

        /// <summary>True iff the request carries an Authorization header (case-insensitive key).</summary>
        internal static bool CarriesAuth(HttpRequestSpec spec)
        {
            if (spec?.Headers == null) return false;
            foreach (var kv in spec.Headers)
                if (string.Equals(kv.Key, "Authorization", StringComparison.OrdinalIgnoreCase))
                    return true;
            return false;
        }
    }
}
