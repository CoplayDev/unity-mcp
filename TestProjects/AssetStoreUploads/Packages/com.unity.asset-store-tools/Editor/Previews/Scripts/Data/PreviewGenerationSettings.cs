namespace AssetStoreTools.Previews.Data
{
    internal abstract class PreviewGenerationSettings
    {
        public abstract GenerationType GenerationType { get; }
        public string[] InputPaths;
        public string OutputPath;
        public PreviewFormat Format;
        public FileNameFormat PreviewFileNamingFormat;
        public bool OverwriteExisting;
    }
}