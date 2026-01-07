using System.IO;

namespace AssetStoreTools.Previews.Data
{
    internal class PreviewMetadata
    {
        public GenerationType Type;
        public string Guid;
        public string Name;
        public string Path;

        public bool Exists()
        {
            return File.Exists(Path);
        }
    }
}