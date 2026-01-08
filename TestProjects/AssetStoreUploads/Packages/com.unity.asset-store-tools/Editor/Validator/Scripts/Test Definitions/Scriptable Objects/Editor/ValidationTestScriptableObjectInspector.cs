using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Utility;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.TestDefinitions
{
    [CustomEditor(typeof(ValidationTestScriptableObject), true)]
    internal class ValidationTestScriptableObjectInspector : UnityEditor.Editor
    {
        private enum FilterSeverity
        {
            Warning,
            Fail
        }

        private enum FilterType
        {
            UseFilter,
            ExcludeFilter
        }

        private ValidationTestScriptableObject _data;
        private ValidationTestScriptableObject[] _allObjects;

        private SerializedProperty _script;
        private SerializedProperty _validationType;

        private SerializedProperty _testScript;
        private SerializedProperty _category;
        private SerializedProperty _failFilterProperty;
        private SerializedProperty _isInclusiveProperty;
        private SerializedProperty _appliesToSubCategories;
        private SerializedProperty _categoryFilter;

        private bool _hadChanges;

        private void OnEnable()
        {
            if (target == null) return;

            _data = target as ValidationTestScriptableObject;

            _script = serializedObject.FindProperty("m_Script");

            _validationType = serializedObject.FindProperty(nameof(ValidationTestScriptableObject.ValidationType));

            _testScript = serializedObject.FindProperty(nameof(ValidationTestScriptableObject.TestScript));
            _category = serializedObject.FindProperty(nameof(ValidationTestScriptableObject.CategoryInfo));
            _failFilterProperty = _category.FindPropertyRelative(nameof(ValidationTestScriptableObject.CategoryInfo.IsFailFilter));
            _isInclusiveProperty = _category.FindPropertyRelative(nameof(ValidationTestScriptableObject.CategoryInfo.IsInclusiveFilter));
            _appliesToSubCategories = _category.FindPropertyRelative(nameof(ValidationTestScriptableObject.CategoryInfo.AppliesToSubCategories));
            _categoryFilter = _category.FindPropertyRelative(nameof(ValidationTestScriptableObject.CategoryInfo.Filter));

            _allObjects = ValidatorUtility.GetAutomatedTestCases(ValidatorUtility.SortType.Id);
            _hadChanges = false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField(GetInspectorTitle(), new GUIStyle(EditorStyles.centeredGreyMiniLabel) { fontSize = 24 }, GUILayout.MinHeight(50));

            EditorGUI.BeginDisabledGroup(true);
            EditorGUILayout.PropertyField(_script);

            EditorGUI.BeginChangeCheck();
            // ID field
            EditorGUILayout.IntField("Test Id", _data.Id);
            if (!ValidateID())
                EditorGUILayout.HelpBox("ID is already in use", MessageType.Warning);
            EditorGUI.EndDisabledGroup();

            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Test Data", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleLeft, fontSize = 14, padding = new RectOffset(0, 0, 0, 0) });

            // Validation Type
            var validationType = (ValidationType)EditorGUILayout.EnumPopup("Validation Type", (ValidationType)_validationType.enumValueIndex);
            _validationType.enumValueIndex = (int)validationType;

            // Other fields
            _data.Title = EditorGUILayout.TextField("Title", _data.Title);
            if (string.IsNullOrEmpty(_data.Title))
                EditorGUILayout.HelpBox("Title cannot be empty", MessageType.Warning);

            EditorGUILayout.LabelField("Description");
            GUIStyle myTextAreaStyle = new GUIStyle(EditorStyles.textArea) { wordWrap = true };
            _data.Description = EditorGUILayout.TextArea(_data.Description, myTextAreaStyle);

            // Test script
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Test Script", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleLeft, fontSize = 14, padding = new RectOffset(0, 0, 0, 0) });

            EditorGUILayout.PropertyField(_testScript);
            if (_testScript.objectReferenceValue != null)
            {
                var generatedScriptType = (_testScript.objectReferenceValue as MonoScript).GetClass();
                if (generatedScriptType == null || !generatedScriptType.GetInterfaces().Contains(typeof(ITestScript)))
                    EditorGUILayout.HelpBox($"Test Script does not derive from {nameof(ITestScript)}. Test execution will fail", MessageType.Warning);
            }
            else if (!string.IsNullOrEmpty(_data.Title))
            {
                var generatedScriptName = GenerateTestScriptName();
                EditorGUILayout.LabelField($"Proposed script name: <i>{generatedScriptName}.cs</i>", new GUIStyle("Label") { richText = true });
                EditorGUILayout.Space();
                EditorGUILayout.BeginHorizontal();
                GUILayout.FlexibleSpace();
                if (GUILayout.Button("Generate Test Method Script", GUILayout.MaxWidth(200f)))
                {
                    var generatedScript = ValidatorUtility.GenerateTestScript(generatedScriptName, (ValidationType)_validationType.enumValueIndex);
                    _testScript.objectReferenceValue = generatedScript;
                }
                EditorGUILayout.EndHorizontal();
            }

            // Variable Sevetity Options
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Variable Severity Status Filtering", new GUIStyle(EditorStyles.centeredGreyMiniLabel) { alignment = TextAnchor.MiddleLeft, fontSize = 14, padding = new RectOffset(0, 0, 0, 0) });

            var filterSeverity = (FilterSeverity)EditorGUILayout.EnumPopup("Fail Type", _failFilterProperty.boolValue ? FilterSeverity.Fail : FilterSeverity.Warning);
            _failFilterProperty.boolValue = filterSeverity == FilterSeverity.Fail ? true : false;
            var filterType = (FilterType)EditorGUILayout.EnumPopup("Filtering rule", _isInclusiveProperty.boolValue ? FilterType.UseFilter : FilterType.ExcludeFilter);
            _isInclusiveProperty.boolValue = filterType == FilterType.UseFilter ? true : false;

            EditorGUILayout.PropertyField(_appliesToSubCategories);

            EditorGUILayout.Space(10);

            EditorGUILayout.BeginHorizontal(GUI.skin.FindStyle("HelpBox"));
            EditorGUILayout.LabelField(GetFilterDescription(_failFilterProperty.boolValue, _isInclusiveProperty.boolValue), new GUIStyle(GUI.skin.label) { wordWrap = true, richText = true });
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.Space(10);

            EditorGUILayout.PropertyField(_categoryFilter);

            if (EditorGUI.EndChangeCheck())
            {
                EditorUtility.SetDirty(target);
                _hadChanges = true;
            }

            _hadChanges = serializedObject.ApplyModifiedProperties() || _hadChanges;
        }

        private string GetInspectorTitle()
        {
            switch (_data)
            {
                case AutomatedTestScriptableObject _:
                    return "Automated Test";
                default:
                    return "Miscellaneous Test";
            }
        }

        private string GenerateTestScriptName()
        {
            var name = _data.Title.Replace(" ", "");
            return name;
        }

        private string GetFilterDescription(bool isFailFilter, bool isInclusive)
        {
            string text = $"When a <i>{TestResultStatus.VariableSeverityIssue}</i> result type is returned from the test method:\n\n";
            if (isFailFilter)
            {
                if (isInclusive)
                    return text + "• <b>Categories IN the filter</b> will result in a <color=red>FAIL</color>.\n• <b>Categories NOT in the filter</b> will result in a <color=yellow>WARNING</color>";
                else
                    return text + "• <b>Categories NOT in the filter</b> will result in a <color=red>FAIL</color>.\n• <b>Categories IN the filter</b> will result in a <color=yellow>WARNING</color>";
            }
            else
            {
                if (isInclusive)
                    return text + "• <b>Categories IN the filter</b> will result in a <color=yellow>WARNING</color>.\n• <b>Categories NOT in the filter</b> will result in a <color=red>FAIL</color>";
                else
                    return text + "• <b>Categories NOT in the filter</b> will result in a <color=yellow>WARNING</color>.\n• <b>Categories IN the filter</b> will result in a <color=red>FAIL</color>";
            }
        }

        private bool ValidateID()
        {
            return !_allObjects.Any(x => x.Id == _data.Id && x != _data);
        }

        private void OnDisable()
        {
            if (!_hadChanges) return;
            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
        }
    }
}