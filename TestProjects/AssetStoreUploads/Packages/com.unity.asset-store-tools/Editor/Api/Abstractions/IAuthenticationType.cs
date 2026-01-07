using AssetStoreTools.Api.Responses;
using System.Threading;
using System.Threading.Tasks;

namespace AssetStoreTools.Api
{
    internal interface IAuthenticationType
    {
        Task<AuthenticationResponse> Authenticate(IAssetStoreClient client, CancellationToken cancellationToken);
    }
}