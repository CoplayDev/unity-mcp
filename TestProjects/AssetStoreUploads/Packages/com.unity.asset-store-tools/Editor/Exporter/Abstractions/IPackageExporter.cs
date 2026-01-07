using System.Threading.Tasks;

namespace AssetStoreTools.Exporter
{
    internal interface IPackageExporter
    {
        PackageExporterSettings Settings { get; }

        Task<PackageExporterResult> Export();
    }
}