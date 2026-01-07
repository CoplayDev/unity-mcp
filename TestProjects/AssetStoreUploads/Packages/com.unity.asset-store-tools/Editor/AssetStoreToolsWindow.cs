using UnityEditor;
using UnityEngine;

namespace AssetStoreTools
{
    internal abstract class AssetStoreToolsWindow : EditorWindow
    {
        protected abstract string WindowTitle { get; }

        private void DefaultInit()
        {
            titleContent = new GUIContent(WindowTitle);
            Init();
        }

        protected abstract void Init();

        private void OnEnable()
        {
            DefaultInit();
        }
    }
}