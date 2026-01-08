using AssetStoreTools.Uploader.Data;
using AssetStoreTools.Utility;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine.UIElements;

namespace AssetStoreTools.Uploader.UI.Elements
{
    internal class HybridPackageWorkflowElement : WorkflowElementBase
    {
        // Data
        private HybridPackageWorkflow _workflow;

        // UI
        private MultiToggleSelectionElement _dependenciesElement;

        public HybridPackageWorkflowElement(HybridPackageWorkflow workflow) : base(workflow)
        {
            _workflow = workflow;

            Create();
            Deserialize();
        }

        private void Create()
        {
            CreatePathElement("Package path", "Select a local Package you would like to export and upload to the Store.");
            CreateDependenciesElement();
            CreatePreviewGenerationElement();
            CreateValidationElement(new CurrentProjectValidationElement(_workflow));
            CreateUploadElement(_workflow, true);
        }

        private void CreateDependenciesElement()
        {
            _dependenciesElement = new MultiToggleSelectionElement()
            {
                ElementLabel = "Dependencies",
                ElementTooltip = "Select which local package dependencies should be included when exporting." +
                "\n\nNote that only local or embedded dependencies defined in the package.json can be selected.",
                NoSelectionLabel = "No packages match this criteria"
            };

            var setDependencies = new Action<Dictionary<string, bool>>((dict) => _workflow.SetDependencies(dict.Where(x => x.Value).Select(x => x.Key), true));
            _dependenciesElement.OnValuesChanged += setDependencies;
            Add(_dependenciesElement);
            _dependenciesElement.style.display = DisplayStyle.None;
        }

        protected override void BrowsePath()
        {
            var absoluteExportPath = EditorUtility.OpenFilePanel("Select a package.json file", "Packages/", "json");
            if (string.IsNullOrEmpty(absoluteExportPath))
                return;

            if (!_workflow.IsPathValid(absoluteExportPath, out var error))
            {
                EditorUtility.DisplayDialog("Invalid selection", error, "OK");
                return;
            }

            HandlePathSelection(absoluteExportPath, true);
            CheckForMissingMetas();
        }

        private void HandlePathSelection(string packageManifestPath, bool serialize)
        {
            if (string.IsNullOrEmpty(packageManifestPath))
                return;

            _workflow.SetPackage(packageManifestPath, serialize);
            var packageFolderPath = _workflow.GetPackage().assetPath;
            SetPathSelectionTextField(packageFolderPath + "/");

            UpdateDependenciesElement();
        }

        private void CheckForMissingMetas()
        {
            var paths = new List<string>() { _workflow.GetPackage().assetPath };
            paths.AddRange(_workflow.GetDependencies().Select(x => x.assetPath));
            CheckForMissingMetas(paths);
        }

        private void UpdateDependenciesElement()
        {
            var availableDependencies = _workflow.GetAvailableDependencies();
            var selectedDependencies = availableDependencies.ToDictionary(x => x.name, y => _workflow.GetDependencies().Any(x => x.name == y.name));
            _dependenciesElement.Populate(selectedDependencies);
            _dependenciesElement.style.display = availableDependencies.Count > 0 ? DisplayStyle.Flex : DisplayStyle.None;
        }

        protected override void EnableInteraction()
        {
            base.EnableInteraction();
            _dependenciesElement.SetEnabled(true);
        }

        protected override void DisableInteraction()
        {
            base.DisableInteraction();
            _dependenciesElement.SetEnabled(false);
        }

        protected override void Deserialize()
        {
            var package = _workflow.GetPackage();
            if (package == null)
                return;

            HandlePathSelection(AssetDatabase.GetAssetPath(package.GetManifestAsset()), false);
        }
    }
}