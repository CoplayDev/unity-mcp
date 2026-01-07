using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class MultiToggleSelectionElement : VisualElement
    {
        // Data
        private Dictionary<string, bool> _selections;
        private readonly List<string> _selectionFilters = new List<string> { "All", "Selected", "Not Selected" };
        private string _activeFilter;

        public bool DisplayElementLabel
        {
            get => _multiToggleSelectionHelpRow.style.visibility == Visibility.Visible;
            set { _multiToggleSelectionHelpRow.style.visibility = value ? Visibility.Visible : Visibility.Hidden; }
        }

        public string ElementLabel { get => _multiToggleSelectionLabel.text; set { _multiToggleSelectionLabel.text = value; } }
        public string ElementTooltip { get => _multiToggleSelectionTooltip.tooltip; set { _multiToggleSelectionTooltip.tooltip = value; } }
        public string NoSelectionLabel { get => _noSelectionsLabel.text; set { _noSelectionsLabel.text = value; } }

        // UI
        private VisualElement _multiToggleSelectionHelpRow;
        private Label _multiToggleSelectionLabel;
        private Image _multiToggleSelectionTooltip;

        private ScrollView _selectionTogglesBox;
        private Label _noSelectionsLabel;
        private ToolbarMenu _filteringDropdown;

        public event Action<Dictionary<string, bool>> OnValuesChanged;

        public MultiToggleSelectionElement()
        {
            _activeFilter = _selectionFilters[0];
            AddToClassList("package-content-option-box");
            Create();
        }

        private void Create()
        {
            _multiToggleSelectionHelpRow = new VisualElement();
            _multiToggleSelectionHelpRow.AddToClassList("package-content-option-label-help-row");

            _multiToggleSelectionLabel = new Label();
            _multiToggleSelectionTooltip = new Image();

            VisualElement fullSelectionBox = new VisualElement();
            fullSelectionBox.AddToClassList("multi-toggle-box");

            _selectionTogglesBox = new ScrollView { name = "DependencyToggles" };
            _selectionTogglesBox.AddToClassList("multi-toggle-box-scrollview");

            _noSelectionsLabel = new Label();
            _noSelectionsLabel.AddToClassList("multi-toggle-box-empty-label");

            var scrollContainer = _selectionTogglesBox.Q<VisualElement>("unity-content-viewport");
            scrollContainer.Add(_noSelectionsLabel);

            VisualElement filteringBox = new VisualElement();
            filteringBox.AddToClassList("multi-toggle-box-toolbar");

            // Select - deselect buttons
            VisualElement selectingBox = new VisualElement();
            selectingBox.AddToClassList("multi-toggle-box-toolbar-selecting-box");

            Button selectAllButton = new Button(SelectAllToggles)
            {
                text = "Select All"
            };

            Button deSelectAllButton = new Button(UnselectAllToggles)
            {
                text = "Deselect All"
            };

            selectingBox.Add(selectAllButton);
            selectingBox.Add(deSelectAllButton);

            // Filtering dropdown
            VisualElement filteringDropdownBox = new VisualElement();
            filteringDropdownBox.AddToClassList("multi-toggle-box-toolbar-filtering-box");

            _filteringDropdown = new ToolbarMenu { text = _selectionFilters[0] };

            foreach (var filter in _selectionFilters)
                _filteringDropdown.menu.AppendAction(filter, (_) => { FilterDependencies(filter); });

            filteringDropdownBox.Add(_filteringDropdown);

            // Final adding
            filteringBox.Add(filteringDropdownBox);
            filteringBox.Add(selectingBox);

            fullSelectionBox.Add(_selectionTogglesBox);
            fullSelectionBox.Add(filteringBox);

            _multiToggleSelectionHelpRow.Add(_multiToggleSelectionLabel);
            _multiToggleSelectionHelpRow.Add(_multiToggleSelectionTooltip);

            Add(_multiToggleSelectionHelpRow);
            Add(fullSelectionBox);
        }

        public void Populate(Dictionary<string, bool> selections)
        {
            _selectionTogglesBox.Clear();
            _selections = selections;

            EventCallback<ChangeEvent<bool>, string> callback = OnToggle;

            foreach (var kvp in selections)
            {
                var toggle = new Toggle() { text = kvp.Key, value = kvp.Value };
                toggle.AddToClassList("multi-toggle-box-toggle");
                toggle.RegisterCallback(callback, toggle.text);
                _selectionTogglesBox.Add(toggle);
            }

            FilterDependencies(_activeFilter);
        }

        private void FilterDependencies(string filter)
        {
            _activeFilter = filter;

            var allToggles = _selectionTogglesBox.Children().Cast<Toggle>().ToArray();
            var selectedIndex = _selectionFilters.FindIndex(x => x == filter);

            switch (selectedIndex)
            {
                case 0:
                    foreach (var toggle in allToggles)
                        toggle.style.display = DisplayStyle.Flex;
                    break;
                case 1:
                    foreach (var toggle in allToggles)
                        toggle.style.display = toggle.value ? DisplayStyle.Flex : DisplayStyle.None;
                    break;
                case 2:
                    foreach (var toggle in allToggles)
                        toggle.style.display = toggle.value ? DisplayStyle.None : DisplayStyle.Flex;
                    break;
            }

            // Check if any toggles are displayed
            var count = allToggles.Count(toggle => toggle.style.display == DisplayStyle.Flex);
            _noSelectionsLabel.style.display = count > 0 ? DisplayStyle.None : DisplayStyle.Flex;

            _filteringDropdown.text = filter;
        }

        private void OnToggle(ChangeEvent<bool> evt, string text)
        {
            FilterDependencies(_activeFilter);
            _selections[text] = evt.newValue;
            OnValuesChanged?.Invoke(_selections);
        }

        private void OnAllToggles(bool value)
        {
            var allToggles = _selectionTogglesBox.Children().Cast<Toggle>();
            foreach (var toggle in allToggles)
                toggle.SetValueWithoutNotify(value);

            foreach (var key in _selections.Keys.ToArray())
                _selections[key] = value;

            FilterDependencies(_activeFilter);
            OnValuesChanged?.Invoke(_selections);
        }

        private void SelectAllToggles()
        {
            OnAllToggles(true);
        }

        private void UnselectAllToggles()
        {
            OnAllToggles(false);
        }
    }
}