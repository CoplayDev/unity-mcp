using AssetStoreTools.Validator.Categories;
using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Utility;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.TestDefinitions
{
    internal abstract class ValidationTestScriptableObject : ScriptableObject
    {
        [SerializeField, HideInInspector]
        private bool HasBeenInitialized;

        public int Id;
        public string Title;
        public string Description;
        public ValidatorCategory CategoryInfo;
        public ValidationType ValidationType;
        public MonoScript TestScript;

        private void OnEnable()
        {
            // To do: maybe replace with Custom Inspector
            if (HasBeenInitialized)
                return;

            var existingTestCases = ValidatorUtility.GetAutomatedTestCases(ValidatorUtility.SortType.Id);
            if (existingTestCases.Length > 0)
                Id = existingTestCases[existingTestCases.Length - 1].Id + 1;
            else
                Id = 1;
            HasBeenInitialized = true;
        }
    }
}