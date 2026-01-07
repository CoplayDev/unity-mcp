using AssetStoreTools.Previews.Data;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Previews.Generators.Custom.TypeGenerators
{
    internal class PrefabTypePreviewGenerator : TypePreviewGeneratorFromScene
    {
        public override event Action<int, int> OnAssetProcessed;

        public PrefabTypePreviewGenerator(TypePreviewGeneratorFromSceneSettings settings) : base(settings) { }

        protected override IEnumerable<UnityEngine.Object> CollectAssets()
        {
            var prefabs = new List<UnityEngine.Object>();
            var prefabGuids = AssetDatabase.FindAssets("t:prefab", Settings.InputPaths);

            foreach (var guid in prefabGuids)
            {
                var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(AssetDatabase.GUIDToAssetPath(guid));

                // Skip nested prefabs
                if (!AssetDatabase.IsMainAsset(prefab))
                    continue;

                // Skip prefabs without renderers
                if (prefab.GetComponentsInChildren<Renderer>().Length == 0)
                    continue;

                prefabs.Add(prefab);
            }

            return prefabs;
        }

        protected override async Task<List<PreviewMetadata>> GeneratePreviewsInScene(IEnumerable<UnityEngine.Object> assets)
        {
            var generatedPreviews = new List<PreviewMetadata>();
            var prefabs = assets.ToList();
            var objectReferenceShader = GetDefaultObjectShader();
            var particleReferenceShader = GetDefaultParticleShader();

            for (int i = 0; i < prefabs.Count; i++)
            {
                ThrowIfSceneChanged();

                var prefab = prefabs[i] as GameObject;
                if (prefab != null)
                {
                    var go = UnityEngine.Object.Instantiate(prefab, Vector3.zero, Quaternion.Euler(0, 0, 0));

                    ReplaceMissingShaders(go, objectReferenceShader, particleReferenceShader);

                    HandleParticleSystems(go);

                    var previewPath = Settings.Screenshotter.Screenshot(go, GenerateOutputPathWithoutExtension(prefab, Settings.PreviewFileNamingFormat));
                    if (!string.IsNullOrEmpty(previewPath))
                        generatedPreviews.Add(ObjectToMetadata(prefab, previewPath));

                    UnityEngine.Object.DestroyImmediate(go);
                }

                OnAssetProcessed?.Invoke(i, prefabs.Count);
                await Task.Yield();
            }

            return generatedPreviews;
        }

        private void ReplaceMissingShaders(GameObject go, Shader objectShader, Shader particleShader)
        {
            var meshRenderers = go.GetComponentsInChildren<Renderer>();
            foreach (var mr in meshRenderers)
            {
                var shaderToUse = mr is ParticleSystemRenderer ? particleShader : objectShader;

                var materialArray = mr.sharedMaterials;
                for (int i = 0; i < materialArray.Length; i++)
                {
                    if (materialArray[i] == null)
                    {
                        materialArray[i] = new Material(shaderToUse);
                    }
                    else if (!materialArray[i].shader.isSupported)
                    {
                        materialArray[i].shader = shaderToUse;
                    }
                }

                mr.sharedMaterials = materialArray;
            }
        }

        private void HandleParticleSystems(GameObject go)
        {
            var particleSystems = go.GetComponentsInChildren<ParticleSystem>();
            if (particleSystems.Length == 0)
                return;

            foreach (var ps in particleSystems)
            {
                ps.Stop();
                ps.Clear();
                ps.randomSeed = 1;
                ps.Simulate(10, false, true, false);
            }
        }
    }
}