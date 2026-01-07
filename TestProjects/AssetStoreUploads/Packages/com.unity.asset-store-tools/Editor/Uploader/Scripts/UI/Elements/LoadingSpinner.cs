using UnityEditor;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class LoadingSpinner : VisualElement
    {
        // Data
        private int _spinIndex;
        private double _spinTimer;
        private double _spinThreshold = 0.1;

        // UI
        private Image _spinnerImage;

        public LoadingSpinner()
        {
            AddToClassList("loading-spinner-box");

            _spinnerImage = new Image { name = "SpinnerImage" };
            _spinnerImage.AddToClassList("loading-spinner-image");

            Add(_spinnerImage);
        }

        public void Show()
        {
            EditorApplication.update += SpinnerLoop;
            style.display = DisplayStyle.Flex;
        }

        public void Hide()
        {
            EditorApplication.update -= SpinnerLoop;
            style.display = DisplayStyle.None;
        }

        private void SpinnerLoop()
        {
            if (_spinTimer + _spinThreshold > EditorApplication.timeSinceStartup)
                return;

            _spinTimer = EditorApplication.timeSinceStartup;
            _spinnerImage.image = EditorGUIUtility.IconContent($"WaitSpin{_spinIndex:00}").image;

            _spinIndex += 1;

            if (_spinIndex > 11)
                _spinIndex = 0;
        }
    }
}
