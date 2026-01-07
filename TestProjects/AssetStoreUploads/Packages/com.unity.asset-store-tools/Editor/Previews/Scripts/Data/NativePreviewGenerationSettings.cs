namespace AssetStoreTools.Previews.Data
{
    internal class NativePreviewGenerationSettings : PreviewGenerationSettings
    {
        public override GenerationType GenerationType => GenerationType.Native;
        public bool WaitForPreviews;
        public bool ChunkedPreviewLoading;
        public int ChunkSize;
    }
}