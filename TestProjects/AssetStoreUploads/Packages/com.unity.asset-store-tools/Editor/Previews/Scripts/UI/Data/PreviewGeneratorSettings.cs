using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Generators;
using AssetStoreTools.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace AssetStoreTools.Previews.UI.Data
{
    internal class PreviewGeneratorSettings : IPreviewGeneratorSettings
    {
        private readonly GenerationType[] _availableGenerationTypes = new GenerationType[]
        {
            GenerationType.Native,
            GenerationType.Custom
        };

        private List<string> _inputPaths;
        private GenerationType _generationType;

        public event Action OnGenerationTypeChanged;
        public event Action OnGenerationPathsChanged;

        public PreviewGeneratorSettings()
        {
            _inputPaths = new List<string>();
            _generationType = GenerationType.Native;
        }

        public void LoadSettings(PreviewGenerationSettings settings)
        {
            if (settings == null)
                return;

            _inputPaths = settings.InputPaths.ToList();
            OnGenerationPathsChanged?.Invoke();

            switch (settings)
            {
                case NativePreviewGenerationSettings _:
                    _generationType = GenerationType.Native;
                    break;
                case CustomPreviewGenerationSettings _:
                    _generationType = GenerationType.Custom;
                    break;
                default:
                    return;
            }

            OnGenerationTypeChanged?.Invoke();
        }

        public GenerationType GetGenerationType()
        {
            return _generationType;
        }

        public void SetGenerationType(GenerationType type)
        {
            _generationType = type;
            OnGenerationTypeChanged?.Invoke();
        }

        public List<GenerationType> GetAvailableGenerationTypes()
        {
            return _availableGenerationTypes.ToList();
        }

        public List<string> GetGenerationPaths()
        {
            return _inputPaths;
        }

        public void AddGenerationPath(string path)
        {
            if (string.IsNullOrEmpty(path))
                return;

            if (_inputPaths.Contains(path))
                return;

            // Prevent redundancy for new paths
            var existingPath = _inputPaths.FirstOrDefault(x => path.StartsWith(x + "/"));
            if (existingPath != null)
            {
                Debug.LogWarning($"Path '{path}' is already included with existing path: '{existingPath}'");
                return;
            }

            // Prevent redundancy for already added paths
            var redundantPaths = _inputPaths.Where(x => x.StartsWith(path + "/")).ToArray();
            foreach (var redundantPath in redundantPaths)
            {
                Debug.LogWarning($"Existing validation path '{redundantPath}' has been made redundant by the inclusion of new validation path: '{path}'");
                _inputPaths.Remove(redundantPath);
            }

            _inputPaths.Add(path);

            OnGenerationPathsChanged?.Invoke();
        }

        public void RemoveGenerationPath(string path)
        {
            if (!_inputPaths.Contains(path))
                return;

            _inputPaths.Remove(path);

            OnGenerationPathsChanged?.Invoke();
        }

        public void ClearGenerationPaths()
        {
            if (_inputPaths.Count == 0)
                return;

            _inputPaths.Clear();

            OnGenerationPathsChanged?.Invoke();
        }

        public bool IsGenerationPathValid(string path, out string error)
        {
            error = string.Empty;

            if (string.IsNullOrEmpty(path))
            {
                error = "Path cannot be empty";
                return false;
            }

            var isAssetsPath = path.StartsWith("Assets/")
                || path.Equals("Assets");
            var isPackagePath = PackageUtility.GetPackageByManifestPath($"{path}/package.json", out _);

            if (!isAssetsPath && !isPackagePath)
            {
                error = "Selected path must be within the Assets folder or point to a root path of a package";
                return false;
            }

            if (!Directory.Exists(path))
            {
                error = "Path does not exist";
                return false;
            }

            if (path.Split('/').Any(x => x.StartsWith(".") || x.EndsWith("~")))
            {
                error = $"Path '{path}' cannot be selected as it is a hidden folder and not part of the Asset Database";
                return false;
            }

            return true;
        }

        public IPreviewGenerator CreateGenerator()
        {
            switch (_generationType)
            {
                case GenerationType.Native:
                    return CreateNativeGenerator();
                case GenerationType.Custom:
                    return CreateCustomGenerator();
                default:
                    throw new NotImplementedException("Undefined generator type");
            }
        }

        private IPreviewGenerator CreateNativeGenerator()
        {
            var settings = new NativePreviewGenerationSettings()
            {
                InputPaths = _inputPaths.ToArray(),
                OutputPath = Constants.Previews.Native.DefaultOutputPath,
                PreviewFileNamingFormat = Constants.Previews.DefaultFileNameFormat,
                Format = Constants.Previews.Native.DefaultFormat,
                WaitForPreviews = Constants.Previews.Native.DefaultWaitForPreviews,
                ChunkedPreviewLoading = Constants.Previews.Native.DefaultChunkedPreviewLoading,
                ChunkSize = Constants.Previews.Native.DefaultChunkSize,
                OverwriteExisting = true
            };

            return new NativePreviewGenerator(settings);
        }

        private IPreviewGenerator CreateCustomGenerator()
        {
            var settings = new CustomPreviewGenerationSettings()
            {
                InputPaths = _inputPaths.ToArray(),
                OutputPath = Constants.Previews.Custom.DefaultOutputPath,
                Width = Constants.Previews.Custom.DefaultWidth,
                Height = Constants.Previews.Custom.DefaultHeight,
                Depth = Constants.Previews.Custom.DefaultDepth,
                NativeWidth = Constants.Previews.Custom.DefaultNativeWidth,
                NativeHeight = Constants.Previews.Custom.DefaultNativeHeight,
                PreviewFileNamingFormat = Constants.Previews.DefaultFileNameFormat,
                Format = Constants.Previews.Custom.DefaultFormat,
                AudioSampleColor = Constants.Previews.Custom.DefaultAudioSampleColor,
                AudioBackgroundColor = Constants.Previews.Custom.DefaultAudioBackgroundColor,
                OverwriteExisting = true
            };

            var generator = new CustomPreviewGenerator(settings);
            return generator;
        }
    }
}