using AssetStoreTools.Previews.Data;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace AssetStoreTools.Exporter
{
    internal class PreviewInjector : IPreviewInjector
    {
        private PreviewGenerationResult _result;

        public PreviewInjector(PreviewGenerationResult result)
        {
            _result = result;
        }

        public void Inject(string temporaryPackagePath)
        {
            if (_result == null || !_result.Success)
                return;

            var previews = _result.Previews.Where(x => x.Type == _result.GenerationType && x.Exists());
            InjectFilesIntoGuidFolders(previews, temporaryPackagePath);
        }

        private void InjectFilesIntoGuidFolders(IEnumerable<PreviewMetadata> previews, string temporaryPackagePath)
        {
            foreach (var assetFolder in Directory.EnumerateDirectories(temporaryPackagePath))
            {
                var guid = assetFolder.Replace("\\", "/").Split('/').Last();
                var generatedPreview = previews.FirstOrDefault(x => x.Guid.Equals(guid));

                if (generatedPreview == null)
                    continue;

                // Note: Unity Importer and Asset Store only operate with .png extensions
                File.Copy(generatedPreview.Path, $"{assetFolder}/preview.png", true);
            }
        }
    }
}