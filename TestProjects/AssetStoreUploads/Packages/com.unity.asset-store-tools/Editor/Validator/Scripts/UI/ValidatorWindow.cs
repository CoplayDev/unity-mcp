using AssetStoreTools.Utility;
using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services;
using AssetStoreTools.Validator.UI.Views;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Validator.UI
{

    internal class ValidatorWindow : AssetStoreToolsWindow
    {
        protected override string WindowTitle => "Asset Store Validator";

        private ICachingService _cachingService;

        private ValidatorTestsView _validationTestsView;

        protected override void Init()
        {
            minSize = new Vector2(350, 350);

            this.SetAntiAliasing(4);

            VisualElement root = rootVisualElement;

            // Clean it out, in case the window gets initialized again
            root.Clear();

            // Getting a reference to the USS Document and adding stylesheet to the root
            root.styleSheets.Add(StyleSelector.ValidatorWindow.ValidatorWindowStyle);
            root.styleSheets.Add(StyleSelector.ValidatorWindow.ValidatorWindowTheme);

            GetServices();
            ConstructWindow();
        }

        private void GetServices()
        {
            _cachingService = ValidatorServiceProvider.Instance.GetService<ICachingService>();
        }

        private void ConstructWindow()
        {
            _validationTestsView = new ValidatorTestsView(_cachingService);
            rootVisualElement.Add(_validationTestsView);
        }

        public void Load(ValidationSettings settings, ValidationResult result)
        {
            _validationTestsView.LoadSettings(settings);
            _validationTestsView.LoadResult(result);
        }
    }
}