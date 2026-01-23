using System;
using MCPForUnity.Editor.Constants;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnity.Editor.Windows.Components.Validation
{
    /// <summary>
    /// Controller for the Script Validation section.
    /// Handles script validation level settings.
    /// </summary>
    public class McpValidationSection
    {
        // UI Elements
        private EnumField validationLevelField;
        private Label validationDescription;

        // Data
        private ValidationLevel currentValidationLevel = ValidationLevel.Standard;

        // Validation levels
        public enum ValidationLevel
        {
            Basic,
            Standard,
            Comprehensive,
            Strict
        }

        public VisualElement Root { get; private set; }

        public McpValidationSection(VisualElement root)
        {
            Root = root;
            CacheUIElements();
            InitializeUI();
            RegisterCallbacks();
        }

        private void CacheUIElements()
        {
            validationLevelField = Root.Q<EnumField>("validation-level");
            validationDescription = Root.Q<Label>("validation-description");
        }

        private void InitializeUI()
        {
            validationLevelField.Init(ValidationLevel.Standard);
            int savedLevel = EditorPrefs.GetInt(EditorPrefKeys.ValidationLevel, 1);
            currentValidationLevel = (ValidationLevel)Mathf.Clamp(savedLevel, 0, 3);
            validationLevelField.value = currentValidationLevel;
            UpdateValidationDescription();
        }

        private void RegisterCallbacks()
        {
            validationLevelField.RegisterValueChangedCallback(evt =>
            {
                currentValidationLevel = (ValidationLevel)evt.newValue;
                EditorPrefs.SetInt(EditorPrefKeys.ValidationLevel, (int)currentValidationLevel);
                UpdateValidationDescription();
            });
        }

        private void UpdateValidationDescription()
        {
            validationDescription.text = currentValidationLevel switch
            {
                ValidationLevel.Basic => "Basic: Validates syntax only. Fast compilation checks.",
                ValidationLevel.Standard => "Standard (Recommended): Checks syntax + common errors. Balanced speed and coverage.",
                ValidationLevel.Comprehensive => "Comprehensive: Detailed validation including code quality. Slower but thorough.",
                ValidationLevel.Strict => "Strict: Maximum validation + warnings as errors. Slowest but catches all issues.",
                _ => "Unknown validation level"
            };
        }
    }
}
