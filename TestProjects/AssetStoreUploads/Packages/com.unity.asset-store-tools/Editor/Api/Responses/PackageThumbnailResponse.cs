using System;
using UnityEngine;

namespace AssetStoreTools.Api.Responses
{
    internal class PackageThumbnailResponse : AssetStoreResponse
    {
        public Texture2D Thumbnail { get; set; }
        public PackageThumbnailResponse() : base() { }
        public PackageThumbnailResponse(Exception e) : base(e) { }

        public PackageThumbnailResponse(byte[] textureBytes)
        {
            try
            {
                var tex = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                var success = tex.LoadImage(textureBytes);
                if (!success)
                    throw new Exception("Could not retrieve image from the provided texture bytes");

                Thumbnail = tex;
                Success = true;
            }
            catch (Exception e)
            {
                Success = false;
                Exception = e;
            }
        }
    }
}