using UnityEngine;

namespace AssetStoreTools.Previews.Data
{
    internal class CustomPreviewGenerationSettings : PreviewGenerationSettings
    {
        public override GenerationType GenerationType => GenerationType.Custom;

        public int Width;
        public int Height;
        public int Depth;

        public int NativeWidth;
        public int NativeHeight;

        public Color AudioSampleColor;
        public Color AudioBackgroundColor;
    }
}