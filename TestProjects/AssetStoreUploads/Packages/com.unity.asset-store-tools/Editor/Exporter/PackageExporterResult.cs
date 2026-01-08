using AssetStoreTools.Previews.Data;
using System;

namespace AssetStoreTools.Exporter
{
    internal class PackageExporterResult
    {
        public bool Success;
        public string ExportedPath;
        public PreviewGenerationResult PreviewGenerationResult;
        public Exception Exception;
    }
}