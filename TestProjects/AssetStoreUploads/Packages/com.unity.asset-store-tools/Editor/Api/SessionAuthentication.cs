using AssetStoreTools.Api.Responses;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Api
{
    internal class SessionAuthentication : AuthenticationBase
    {
        public SessionAuthentication(string sessionId)
        {
            AuthenticationContent = GetAuthenticationContent(
                new KeyValuePair<string, string>("reuse_session", sessionId)
                );
        }

        public override async Task<AuthenticationResponse> Authenticate(IAssetStoreClient client, CancellationToken cancellationToken)
        {
            var result = await client.Post(LoginUrl, AuthenticationContent, cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();

            return ParseResponse(result);
        }
    }
}