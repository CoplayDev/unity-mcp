using AssetStoreTools.Previews.Generators;

namespace AssetStoreTools.Exporter
{
    internal class DefaultExporterSettings : PackageExporterSettings
    {
        public string[] ExportPaths;
        public string[] Dependencies;
        public IPreviewGenerator PreviewGenerator;
    }
}