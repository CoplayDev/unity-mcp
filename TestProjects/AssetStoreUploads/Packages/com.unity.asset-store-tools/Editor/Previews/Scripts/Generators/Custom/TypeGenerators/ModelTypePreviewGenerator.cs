using AssetStoreTools.Previews.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Previews.Generators.Custom.TypeGenerators
{
    internal class ModelTypePreviewGenerator : TypePreviewGeneratorFromScene
    {
        public override event Action<int, int> OnAssetProcessed;

        public ModelTypePreviewGenerator(TypePreviewGeneratorFromSceneSettings settings) : base(settings) { }

        protected override IEnumerable<UnityEngine.Object> CollectAssets()
        {
            var models = new List<UnityEngine.Object>();
            var modelGuids = AssetDatabase.FindAssets("t:model", Settings.InputPaths);

            foreach (var guid in modelGuids)
            {
                var model = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));

                // Skip nested models
                if (!AssetDatabase.IsMainAsset(model))
                    continue;

                // Skip models without renderers
                if (model.GetComponentsInChildren<Renderer>().Length == 0)
                    continue;

                models.Add(model);
            }

            return models;
        }

        protected override async Task<List<PreviewMetadata>> GeneratePreviewsInScene(IEnumerable<UnityEngine.Object> assets)
        {
            var generatedPreviews = new List<PreviewMetadata>();
            var models = assets.ToList();
            var referenceShader = GetDefaultObjectShader();

            for (int i = 0; i < models.Count; i++)
            {
                ThrowIfSceneChanged();

                var model = models[i] as GameObject;

                if (model != null)
                {
                    var go = UnityEngine.Object.Instantiate(model, Vector3.zero, Quaternion.Euler(0, 0, 0));
                    ReplaceShaders(go, referenceShader);

                    var previewPath = Settings.Screenshotter.Screenshot(go, GenerateOutputPathWithoutExtension(model, Settings.PreviewFileNamingFormat));
                    if (!string.IsNullOrEmpty(previewPath))
                        generatedPreviews.Add(ObjectToMetadata(model, previewPath));

                    UnityEngine.Object.DestroyImmediate(go);
                }

                OnAssetProcessed?.Invoke(i, models.Count);
                await Task.Yield();
            }

            return generatedPreviews;
        }

        private void ReplaceShaders(GameObject go, Shader shader)
        {
            var meshRenderers = go.GetComponentsInChildren<Renderer>();
            foreach (var mr in meshRenderers)
            {
                var materialArray = mr.sharedMaterials;
                for (int i = 0; i < materialArray.Length; i++)
                {
                    materialArray[i] = new Material(shader) { color = new Color(0.7f, 0.7f, 0.7f) };
                }

                mr.sharedMaterials = materialArray;
            }
        }
    }
}