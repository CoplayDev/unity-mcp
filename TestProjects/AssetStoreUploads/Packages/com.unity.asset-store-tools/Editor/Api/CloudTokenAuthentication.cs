using AssetStoreTools.Api.Responses;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Api
{
    internal class CloudTokenAuthentication : AuthenticationBase
    {
        public CloudTokenAuthentication(string cloudToken)
        {
            AuthenticationContent = GetAuthenticationContent(
                new KeyValuePair<string, string>("user_access_token", cloudToken)
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
