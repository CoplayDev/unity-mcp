using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class AssetsWorkflowElement : WorkflowElementBase
    {
        // Data
        private AssetsWorkflow _workflow;

        // UI
        private VisualElement _dependenciesToggleElement;
        private Toggle _dependenciesToggle;
        private MultiToggleSelectionElement _dependenciesElement;
        private MultiToggleSelectionElement _specialFoldersElement;

        private const string PathSelectionTooltip = "Select the main folder of your package" +
            "\n\nAll files and folders of your package should preferably be contained within a single root folder that is named after your package" +
            "\n\nExample: 'Assets/[MyPackageName]'" +
            "\n\nNote: If your content makes use of special folders that are required to be placed in the root Assets folder (e.g. 'StreamingAssets')," +
            " you will be able to include them after selecting the main folder";

        public AssetsWorkflowElement(AssetsWorkflow workflow) : base(workflow)
        {
            _workflow = workflow;

            Create();
            Deserialize();
        }

        private void Create()
        {
            CreatePathElement("Folder path", PathSelectionTooltip);
            CreateDependenciesToggleElement();
            CreateDependenciesSelectionElement();
            CreateSpecialFoldersElement();
            CreatePreviewGenerationElement();
            CreateValidationElement(new CurrentProjectValidationElement(_workflow));
            CreateUploadElement(_workflow, true);
        }

        private void CreateDependenciesToggleElement()
        {
            _dependenciesToggleElement = new VisualElement() { name = "Dependencies Toggle" };
            _dependenciesToggleElement.AddToClassList("package-content-option-box");

            VisualElement dependenciesLabelHelpRow = new VisualElement();
            dependenciesLabelHelpRow.AddToClassList("package-content-option-label-help-row");

            Label dependenciesLabel = new Label { text = "Dependencies" };
            Image dependenciesLabelTooltip = new Image
            {
                tooltip = "Tick this checkbox if your package content has dependencies on Unity packages from the Package Manager"
            };

            _dependenciesToggle = new Toggle { name = "DependenciesToggle", text = "Include Package Manifest" };
            _dependenciesToggle.AddToClassList("package-content-option-toggle");

            var callback = new Action(() => DependencyToggleValueChange(true));
            _dependenciesToggle.RegisterValueChangedCallback((_) => DependencyToggleValueChange(true));
            RegisterCallback<AttachToPanelEvent>((_) => { ASToolsPreferences.OnSettingsChange += callback; });
            RegisterCallback<DetachFromPanelEvent>((_) => { ASToolsPreferences.OnSettingsChange -= callback; });

            dependenciesLabelHelpRow.Add(dependenciesLabel);
            dependenciesLabelHelpRow.Add(dependenciesLabelTooltip);

            _dependenciesToggleElement.Add(dependenciesLabelHelpRow);
            _dependenciesToggleElement.Add(_dependenciesToggle);

            _dependenciesToggleElement.style.display = DisplayStyle.None;
            Add(_dependenciesToggleElement);
        }

        private void CreateDependenciesSelectionElement()
        {
            _dependenciesElement = new MultiToggleSelectionElement()
            {
                DisplayElementLabel = false,
                ElementLabel = "Dependencies",
                NoSelectionLabel = "No packages match this criteria"
            };

            var setDependencies = new Action<Dictionary<string, bool>>((dict) => _workflow.SetDependencies(dict.Where(x => x.Value).Select(x => x.Key), true));
            _dependenciesElement.OnValuesChanged += setDependencies;
            _dependenciesElement.style.display = DisplayStyle.None;
            Add(_dependenciesElement);
        }

        private void CreateSpecialFoldersElement()
        {
            _specialFoldersElement = new MultiToggleSelectionElement()
            {
                ElementLabel = "Special Folders",
                ElementTooltip = "If your package content relies on Special Folders (e.g. StreamingAssets), please select which of these folders should be included in the package.",
                NoSelectionLabel = "No folders match this criteria."
            };

            var setSpecialFolders = new Action<Dictionary<string, bool>>((dict) => _workflow.SetSpecialFolders(dict.Where(x => x.Value).Select(x => x.Key), true));
            _specialFoldersElement.OnValuesChanged += setSpecialFolders;
            _specialFoldersElement.style.display = DisplayStyle.None;
            Add(_specialFoldersElement);
        }

        protected override void BrowsePath()
        {
            string absoluteExportPath = string.Empty;
            bool includeAllAssets = false;

            if (_workflow.IsCompleteProject)
            {
                includeAllAssets = EditorUtility.DisplayDialog("Notice",
                    "Your package draft is set to a category that is treated" +
                    " as a complete project. Project settings will be included automatically. Would you like everything in the " +
                    "'Assets' folder to be included?\n\nYou will still be able to change the selected assets before uploading",
                    "Yes, include all folders and assets",
                    "No, I'll select what to include manually");

                if (includeAllAssets)
                    absoluteExportPath = Application.dataPath;
            }

            if (!includeAllAssets)
            {
                absoluteExportPath = EditorUtility.OpenFolderPanel(
                    "Select folder to compress into a package", "Assets/", "");

                if (string.IsNullOrEmpty(absoluteExportPath))
                    return;
            }

            var relativeExportPath = FileUtility.AbsolutePathToRelativePath(absoluteExportPath, ASToolsPreferences.Instance.EnableSymlinkSupport);
            if (!_workflow.IsPathValid(relativeExportPath, out var error))
            {
                EditorUtility.DisplayDialog("Invalid selection", error, "OK");
                return;
            }

            HandlePathSelection(relativeExportPath, true);
            CheckForMissingMetas();
        }

        private void HandlePathSelection(string relativeExportPath, bool serialize)
        {
            if (string.IsNullOrEmpty(relativeExportPath))
                return;

            _workflow.SetMainExportPath(relativeExportPath, serialize);
            SetPathSelectionTextField(relativeExportPath + "/");

            _dependenciesToggleElement.style.display = DisplayStyle.Flex;
            UpdateSpecialFoldersElement();
        }

        private void CheckForMissingMetas()
        {
            var paths = new List<string>() { _workflow.GetMainExportPath() };
            paths.AddRange(_workflow.GetSpecialFolders());
            CheckForMissingMetas(paths);
        }

        private void DependencyToggleValueChange(bool serialize)
        {
            _workflow.SetIncludeDependencies(_dependenciesToggle.value, serialize);

            if (_dependenciesToggle.value && !ASToolsPreferences.Instance.UseLegacyExporting)
            {
                var allDependencies = _workflow.GetAvailableDependencies();
                var selectedDependencies = allDependencies.ToDictionary(x => x, y => _workflow.GetDependencies().Any(x => x.name == y));
                _dependenciesElement.Populate(selectedDependencies);
                _dependenciesElement.style.display = DisplayStyle.Flex;
            }
            else
            {
                _dependenciesElement.style.display = DisplayStyle.None;
            }
        }

        private void UpdateSpecialFoldersElement()
        {
            var availableSpecialFolders = _workflow.GetAvailableSpecialFolders();
            var selectedSpecialFolders = availableSpecialFolders.ToDictionary(x => x, y => _workflow.GetSpecialFolders().Any(x => x == y));
            _specialFoldersElement.Populate(selectedSpecialFolders);
            _specialFoldersElement.style.display = availableSpecialFolders.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        protected override void EnableInteraction()
        {
            base.EnableInteraction();
            _dependenciesToggleElement.SetEnabled(true);
            _dependenciesElement.SetEnabled(true);
            _specialFoldersElement.SetEnabled(true);
        }

        protected override void DisableInteraction()
        {
            base.DisableInteraction();
            _dependenciesToggleElement.SetEnabled(false);
            _dependenciesElement.SetEnabled(false);
            _specialFoldersElement.SetEnabled(false);
        }

        protected override void Deserialize()
        {
            HandlePathSelection(_workflow.GetMainExportPath(), false);
            _dependenciesToggle.SetValueWithoutNotify(_workflow.GetIncludeDependencies());
            DependencyToggleValueChange(false);
        }
    }
}