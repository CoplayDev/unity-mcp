using System;
using UnityEngine;
using PackageModel = AssetStoreTools.Api.Models.Package;

namespace AssetStoreTools.Uploader.Data
{
    internal interface IPackage
    {
        string PackageId { get; }
        string VersionId { get; }
        string Name { get; }
        string Status { get; }
        string Category { get; }
        bool IsCompleteProject { get; }
        string RootGuid { get; }
        string RootPath { get; }
        string ProjectPath { get; }
        string Modified { get; }
        string Size { get; }
        bool IsDraft { get; }
        Texture2D Icon { get; }

        event Action OnUpdate;
        event Action OnIconUpdate;

        string FormattedSize();
        string FormattedModified();

        void UpdateData(PackageModel source);
        void UpdateIcon(Texture2D texture);

        PackageModel ToModel();
    }
}