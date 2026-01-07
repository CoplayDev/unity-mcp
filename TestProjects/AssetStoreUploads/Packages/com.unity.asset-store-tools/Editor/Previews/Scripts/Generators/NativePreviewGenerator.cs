using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;
using UnityEngine.Tilemaps;

namespace AssetStoreTools.Previews.Generators
{
    internal class NativePreviewGenerator : PreviewGeneratorBase
    {
        private const double InitialPreviewLoadingTimeoutSeconds = 10;

        private NativePreviewGenerationSettings _nativeSettings;

        private RenderTexture _renderTexture;

        private int _generatedPreviewsCount;
        private int _totalPreviewsCount;

        public override event Action<float> OnProgressChanged;

        public NativePreviewGenerator(NativePreviewGenerationSettings settings)
            : base(settings)
        {
            _nativeSettings = settings;
        }

        protected override void Validate()
        {
            base.Validate();

            if (_nativeSettings.ChunkSize <= 0)
                throw new ArgumentException("Chunk size must be larger than 0");
        }

        protected override async Task<PreviewGenerationResult> GenerateImpl()
        {
            var result = new PreviewGenerationResult()
            {
                GenerationType = _nativeSettings.GenerationType
            };

            OnProgressChanged?.Invoke(0f);

            try
            {
                var objects = GetObjectsRequiringPreviews(_nativeSettings.InputPaths);
                var filteredObjects = new List<PreviewMetadata>();
                var reusedPreviews = new List<PreviewMetadata>();
                FilterObjects(objects, filteredObjects, reusedPreviews);

                _generatedPreviewsCount = 0;
                _totalPreviewsCount = objects.Count;

                Directory.CreateDirectory(_nativeSettings.OutputPath);

                var generatedPreviews = new List<PreviewMetadata>();
                if (!_nativeSettings.WaitForPreviews)
                {
                    WritePreviewsWithoutWaiting(filteredObjects, out generatedPreviews);
                }
                else
                {
                    if (_nativeSettings.ChunkedPreviewLoading)
                    {
                        await WaitAndWritePreviewsChunked(filteredObjects, generatedPreviews);
                    }
                    else
                    {
                        await WaitAndWritePreviews(filteredObjects, generatedPreviews);
                    }
                }

                var allPreviews = new List<PreviewMetadata>();
                allPreviews.AddRange(generatedPreviews);
                allPreviews.AddRange(reusedPreviews);

                result.Success = true;
                result.GeneratedPreviews = generatedPreviews;
                result.Previews = allPreviews;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Exception = e;
            }

            return result;
        }

        private List<PreviewMetadata> GetObjectsRequiringPreviews(string[] inputPaths)
        {
            var objects = new List<PreviewMetadata>();
            var guids = AssetDatabase.FindAssets("", inputPaths);

            foreach (var guid in guids)
            {
                if (objects.Any(x => x.Guid == guid))
                    continue;

                var assetPath = AssetDatabase.GUIDToAssetPath(guid);
                var obj = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(assetPath);
                if (!ShouldHavePreview(obj))
                    continue;

                objects.Add(new PreviewMetadata() { Type = GenerationType.Native, Guid = guid });
            }

            return objects;
        }

        private void FilterObjects(List<PreviewMetadata> objects, List<PreviewMetadata> filteredObjects, List<PreviewMetadata> reusedPreviews)
        {
            if (Settings.OverwriteExisting || !CachingService.GetCachedMetadata(out var database))
            {
                filteredObjects.AddRange(objects);
                return;
            }

            foreach (var obj in objects)
            {
                var matchingEntry = database.Previews.FirstOrDefault(x =>
                x.Guid == obj.Guid
                && x.Type == GenerationType.Native
                && x.Exists());

                if (matchingEntry == null)
                {
                    filteredObjects.Add(obj);
                }
                else
                {
                    reusedPreviews.Add(matchingEntry);
                }
            }
        }

        private bool ShouldHavePreview(UnityEngine.Object asset)
        {
            if (asset == null)
                return false;

            if (!AssetDatabase.IsMainAsset(asset))
                return false;

            switch (asset)
            {
                case AudioClip _:
                case Material _:
                case Mesh _:
                case TerrainLayer _:
                case Texture _:
                case Tile _:
                    return true;
                case GameObject go:
                    var renderers = go.GetComponentsInChildren<Renderer>();
                    return renderers != null && renderers.Length > 0;
                default:
                    return false;
            }
        }

        private PreviewMetadata WritePreviewToDisk(PreviewMetadata metadata, Texture2D texture)
        {
            var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(metadata.Guid));
            var width = Mathf.Min(texture.width, 128);
            var height = Mathf.Min(texture.height, 128);
            var readableTexture = GraphicsUtility.ResizeTexture(texture, width, height);
            var fileName = PreviewConvertUtility.ConvertFilenameWithExtension(asset, _nativeSettings.PreviewFileNamingFormat, _nativeSettings.Format);
            var filePath = $"{_nativeSettings.OutputPath}/{fileName}";
            var bytes = PreviewConvertUtility.ConvertTexture(readableTexture, _nativeSettings.Format);

            File.WriteAllBytes(filePath, bytes);

            metadata.Type = GenerationType.Native;
            metadata.Name = asset.name;
            metadata.Path = filePath;

            return metadata;
        }

        private void WritePreviewsWithoutWaiting(List<PreviewMetadata> objects, out List<PreviewMetadata> generatedPreviews)
        {
            generatedPreviews = new List<PreviewMetadata>();

            foreach (var obj in objects)
            {
                var texture = GetAssetPreviewFromGuid(obj.Guid);
                if (texture == null)
                    continue;

                var generatedPreview = WritePreviewToDisk(obj, texture);
                generatedPreviews.Add(generatedPreview);
            }
        }

        private Texture2D GetAssetPreviewFromGuid(string guid)
        {
            var method = typeof(AssetPreview).GetMethod("GetAssetPreviewFromGUID", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static, null, new[] { typeof(string) }, null);
            var args = new object[] { guid };

            return method?.Invoke(null, args) as Texture2D;
        }

        private async Task WaitAndWritePreviewsChunked(List<PreviewMetadata> objects, List<PreviewMetadata> generatedPreviews)
        {
            var chunks = objects.Count / _nativeSettings.ChunkSize;
            var remainder = objects.Count % _nativeSettings.ChunkSize;
            if (remainder != 0)
                chunks += 1;

            for (int i = 0; i < chunks; i++)
            {
                var chunkObjects = new List<PreviewMetadata>();

                for (int j = 0; j < _nativeSettings.ChunkSize; j++)
                {
                    var index = i * _nativeSettings.ChunkSize + j;
                    if (index == objects.Count)
                        break;

                    chunkObjects.Add(objects[index]);
                }

                var generatedPreviewsChunk = new List<PreviewMetadata>();
                await WaitAndWritePreviews(chunkObjects, generatedPreviewsChunk);
                generatedPreviews.AddRange(generatedPreviewsChunk);
            }
        }

        private async Task WaitAndWritePreviews(List<PreviewMetadata> objects, List<PreviewMetadata> generatedPreviews)
        {
            var initialObjectCount = objects.Count();
            if (initialObjectCount == 0)
                return;

            await WaitAndWritePreviewIteration(objects, generatedPreviews);
            var remainingObjectCount = objects.Count;

            // First iteration may take longer to start loading objects
            var firstIterationStartTime = EditorApplication.timeSinceStartup;
            while (true)
            {
                if (remainingObjectCount < initialObjectCount)
                    break;

                if (EditorApplication.timeSinceStartup - firstIterationStartTime > InitialPreviewLoadingTimeoutSeconds)
                    throw new Exception("Preview loading timed out.");

                await WaitAndWritePreviewIteration(objects, generatedPreviews);
                remainingObjectCount = objects.Count;
            }

            if (remainingObjectCount == 0)
                return;

            while (true)
            {
                await WaitForEndOfFrame(1);
                await WaitAndWritePreviewIteration(objects, generatedPreviews);

                // If no more previews are being loaded, try one more time before quitting
                if (objects.Count == remainingObjectCount)
                {
                    await WaitForEndOfFrame(1);
                    await WaitAndWritePreviewIteration(objects, generatedPreviews);

                    if (objects.Count == remainingObjectCount)
                    {
                        var missingObjects = string.Join("\n", objects.Select(x => AssetDatabase.GUIDToAssetPath(x.Guid)));
                        Debug.LogWarning($"Unity Editor failed to fetch previews for {objects.Count} objects:\n{missingObjects}");
                        break;
                    }
                }

                remainingObjectCount = objects.Count;

                // Exit when all previews are loaded
                if (remainingObjectCount == 0)
                    break;
            }
        }

        private async Task WaitAndWritePreviewIteration(List<PreviewMetadata> objects, List<PreviewMetadata> generatedPreviews)
        {
            var cacheSize = Mathf.Max(_nativeSettings.ChunkSize * 2, objects.Count() + _nativeSettings.ChunkSize);
            AssetPreview.SetPreviewTextureCacheSize(cacheSize);

            // Initial queueing
            foreach (var obj in objects)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(obj.Guid));
                AssetPreview.GetAssetPreview(asset);
            }

            await WaitForEndOfFrame();

            // Waiting (NOTE: works inconsistently across Unity streams)
            foreach (var obj in objects)
            {
                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(obj.Guid));
                if (AssetPreview.IsLoadingAssetPreview(asset.GetInstanceID()))
                {
                    await WaitForEndOfFrame();
                }
            }

            // Writing
            for (int i = 0; i < objects.Count; i++)
            {
                var obj = objects[i];

                var asset = AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(AssetDatabase.GUIDToAssetPath(obj.Guid));
                var texture = AssetPreview.GetAssetPreview(asset);
                if (texture == null)
                    continue;

                WritePreviewToDisk(obj, texture);
                generatedPreviews.Add(obj);
                _generatedPreviewsCount++;
                OnProgressChanged?.Invoke((float)_generatedPreviewsCount / _totalPreviewsCount);
            }

            // Removing written objects from the list
            for (int i = objects.Count - 1; i >= 0; i--)
            {
                if (objects[i].Exists())
                    objects.RemoveAt(i);
            }
        }

        private async Task WaitForEndOfFrame(double atLeastSeconds)
        {
            var startTime = EditorApplication.timeSinceStartup;
            while (EditorApplication.timeSinceStartup - startTime <= atLeastSeconds)
            {
                await WaitForEndOfFrame();
            }
        }

        private async Task WaitForEndOfFrame()
        {
            var isNextFrame = false;
            EditorApplication.CallbackFunction callback = null;
            callback = () =>
            {
                EditorApplication.update -= callback;
                isNextFrame = true;
            };

            EditorApplication.update += callback;
            while (!isNextFrame)
                await Task.Yield();
        }
    }
}