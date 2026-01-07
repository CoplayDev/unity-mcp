using AssetStoreTools.Api.Models;
using AssetStoreTools.Api.Responses;
using System.Threading.Tasks;

namespace AssetStoreTools.Uploader.Services.Api
{
    internal interface IAuthenticationService : IUploaderService
    {
        User User { get; }
        Task<AuthenticationResponse> AuthenticateWithCredentials(string email, string password);
        Task<AuthenticationResponse> AuthenticateWithSessionToken();
        Task<AuthenticationResponse> AuthenticateWithCloudToken();
        bool CloudAuthenticationAvailable(out string username, out string cloudToken);
        void Deauthenticate();
    }
}