using System;
using System.Threading.Tasks;
using UnityEngine;

namespace AssetStoreTools.Previews.UI.Data
{
    internal interface IAssetPreview
    {
        UnityEngine.Object Asset { get; }
        string GetAssetPath();
        Task LoadImage(Action<Texture2D> onSuccess);
    }
}