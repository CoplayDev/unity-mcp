using AssetStoreTools.Previews.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Previews.Generators.Custom.TypeGenerators
{
    internal class MaterialTypePreviewGenerator : TypePreviewGeneratorFromScene
    {
        public override event Action<int, int> OnAssetProcessed;

        public MaterialTypePreviewGenerator(TypePreviewGeneratorFromSceneSettings settings) : base(settings) { }

        protected override IEnumerable<UnityEngine.Object> CollectAssets()
        {
            var assets = new List<UnityEngine.Object>();
            var materialGuids = AssetDatabase.FindAssets("t:material", Settings.InputPaths);
            foreach (var guid in materialGuids)
            {
                var mat = AssetDatabase.LoadAssetAtPath<Material>(AssetDatabase.GUIDToAssetPath(guid));

                // Skip nested materials
                if (!AssetDatabase.IsMainAsset(mat))
                    continue;

                // Skip materials with an error shader
                if (IsShaderInvalid(mat.shader))
                {
                    Debug.LogWarning($"Material '{mat}' is using an erroring shader. Preview will not be generated.");
                    continue;
                }

                assets.Add(mat);
            }

            return assets;
        }

        protected override async Task<List<PreviewMetadata>> GeneratePreviewsInScene(IEnumerable<UnityEngine.Object> assets)
        {
            var generatedPreviews = new List<PreviewMetadata>();
            var materials = assets.ToList();
            var sphere = GameObject.CreatePrimitive(PrimitiveType.Sphere);

            var hasMeshRenderer = sphere.TryGetComponent<Renderer>(out var meshRenderer);
            if (!hasMeshRenderer)
                throw new Exception($"Could not find a MeshRenderer for {sphere}");

            for (int i = 0; i < materials.Count; i++)
            {
                ThrowIfSceneChanged();

                var material = materials[i] as Material;

                if (material != null)
                {
                    meshRenderer.sharedMaterial = material;
                    var previewPath = Settings.Screenshotter.Screenshot(sphere, GenerateOutputPathWithoutExtension(material, Settings.PreviewFileNamingFormat));
                    if (!string.IsNullOrEmpty(previewPath))
                        generatedPreviews.Add(ObjectToMetadata(material, previewPath));
                }

                OnAssetProcessed?.Invoke(i, materials.Count);
                await Task.Yield();
            }

            UnityEngine.Object.DestroyImmediate(sphere);
            return generatedPreviews;
        }

        private bool IsShaderInvalid(Shader shader)
        {
            if (ShaderUtil.ShaderHasError(shader))
                return true;

            if (!shader.isSupported)
                return true;

            return false;
        }
    }
}