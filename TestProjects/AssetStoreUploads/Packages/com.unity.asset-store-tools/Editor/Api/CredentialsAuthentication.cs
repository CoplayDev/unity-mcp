using AssetStoreTools.Api.Responses;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Api
{
    internal class CredentialsAuthentication : AuthenticationBase
    {
        public CredentialsAuthentication(string email, string password)
        {
            AuthenticationContent = GetAuthenticationContent(
                new KeyValuePair<string, string>("user", email),
                new KeyValuePair<string, string>("pass", password)
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