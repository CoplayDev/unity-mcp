using AssetStoreTools.Api.Models;
using System;

namespace AssetStoreTools.Api.Responses
{
    internal class RefreshedPackageDataResponse : AssetStoreResponse
    {
        public Package Package { get; set; }
        public RefreshedPackageDataResponse() { }
        public RefreshedPackageDataResponse(Exception e) : base(e) { }
    }
}