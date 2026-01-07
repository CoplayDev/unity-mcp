using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Services;
using AssetStoreTools.Previews.UI.Views;
using AssetStoreTools.Utility;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Previews.UI
{
    internal class PreviewGeneratorWindow : AssetStoreToolsWindow
    {
        protected override string WindowTitle => "Preview Generator";

        private ICachingService _cachingService;

        private PreviewListView _previewListView;

        protected override void Init()
        {
            minSize = new Vector2(350, 350);

            this.SetAntiAliasing(4);

            VisualElement root = rootVisualElement;

            // Getting a reference to the USS Document and adding stylesheet to the root
            root.styleSheets.Add(StyleSelector.PreviewGeneratorWindow.PreviewGeneratorWindowStyle);
            root.styleSheets.Add(StyleSelector.PreviewGeneratorWindow.PreviewGeneratorWindowTheme);

            GetServices();
            ConstructWindow();
        }

        private void GetServices()
        {
            _cachingService = PreviewServiceProvider.Instance.GetService<ICachingService>();
        }

        private void ConstructWindow()
        {
            _previewListView = new PreviewListView(_cachingService);
            rootVisualElement.Add(_previewListView);
        }

        public void Load(PreviewGenerationSettings settings)
        {
            _previewListView.LoadSettings(settings);
        }
    }
}