using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Generators;
using System;
using System.Collections.Generic;

namespace AssetStoreTools.Previews.UI.Data
{
    internal interface IPreviewGeneratorSettings
    {
        event Action OnGenerationTypeChanged;
        event Action OnGenerationPathsChanged;

        void LoadSettings(PreviewGenerationSettings settings);

        GenerationType GetGenerationType();
        void SetGenerationType(GenerationType type);
        List<GenerationType> GetAvailableGenerationTypes();

        List<string> GetGenerationPaths();
        void AddGenerationPath(string path);
        void RemoveGenerationPath(string path);
        void ClearGenerationPaths();
        bool IsGenerationPathValid(string path, out string error);

        IPreviewGenerator CreateGenerator();
    }
}