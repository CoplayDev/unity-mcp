using AssetStoreTools.Previews.Data;

namespace AssetStoreTools.Previews.Generators.Custom.TypeGenerators
{
    internal abstract class TypeGeneratorSettings
    {
        public string[] InputPaths;
        public string[] IgnoredGuids;
        public string OutputPath;
        public FileNameFormat PreviewFileNamingFormat;
    }
}