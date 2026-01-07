using AssetStoreTools.Previews.Data;
using AssetStoreTools.Utility;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace AssetStoreTools.Previews.Services
{
    internal class CachingService : ICachingService
    {
        public void CacheMetadata(IEnumerable<PreviewMetadata> previews)
        {
            var updatedDatabase = UpdatePreviewDatabase(previews);

            var serializerSettings = new JsonSerializerSettings()
            {
                ContractResolver = PreviewDatabaseContractResolver.Instance,
                Converters = new List<JsonConverter>() { new StringEnumConverter() },
                Formatting = Formatting.Indented
            };

            CacheUtil.CreateFileInTempCache(Constants.Previews.PreviewDatabaseFile, JsonConvert.SerializeObject(updatedDatabase, serializerSettings), true);
        }

        public bool GetCachedMetadata(out PreviewDatabase previewDatabase)
        {
            previewDatabase = null;
            if (!CacheUtil.GetFileFromTempCache(Constants.Previews.PreviewDatabaseFile, out string filePath))
                return false;

            try
            {
                var serializerSettings = new JsonSerializerSettings()
                {
                    ContractResolver = PreviewDatabaseContractResolver.Instance,
                    Converters = new List<JsonConverter>() { new StringEnumConverter() }
                };

                previewDatabase = JsonConvert.DeserializeObject<PreviewDatabase>(File.ReadAllText(filePath, Encoding.UTF8), serializerSettings);
                return true;
            }
            catch
            {
                return false;
            }
        }

        private PreviewDatabase UpdatePreviewDatabase(IEnumerable<PreviewMetadata> previews)
        {
            PreviewDatabase database;
            if (!GetCachedMetadata(out database))
                database = new PreviewDatabase();

            // Delete missing previews
            for (int i = database.Previews.Count - 1; i >= 0; i--)
            {
                if (database.Previews[i].Exists())
                    continue;

                database.Previews.RemoveAt(i);
            }

            // Append new previews & Replace existing previews
            foreach (var preview in previews)
            {
                var matchingPreviews = database.Previews.Where(x => x.Guid == preview.Guid).ToList();
                foreach (var matchingPreview in matchingPreviews)
                {
                    // Delete previously generated preview of the same type
                    if (matchingPreview.Type == preview.Type)
                        database.Previews.Remove(matchingPreview);
                    // Delete previously generated preview of a different type if the path matches
                    else if (matchingPreview.Path.Equals(preview.Path))
                        database.Previews.Remove(matchingPreview);
                }

                database.Previews.Add(preview);
            }

            database.Previews = database.Previews.OrderBy(x => x.Guid).ThenBy(x => x.Type).ToList();
            return database;
        }
    }
}