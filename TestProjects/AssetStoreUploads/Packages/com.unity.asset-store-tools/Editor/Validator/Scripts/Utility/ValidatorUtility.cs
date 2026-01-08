using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using static AssetStoreTools.Constants;
using ValidatorConstants = AssetStoreTools.Constants.Validator;

namespace AssetStoreTools.Validator.Utility
{
    internal static class ValidatorUtility
    {
        public enum SortType
        {
            Id,
            Alphabetical
        }

        public static ValidationTestScriptableObject[] GetAutomatedTestCases() => GetAutomatedTestCases(SortType.Id);

        public static ValidationTestScriptableObject[] GetAutomatedTestCases(SortType sortType)
        {
            string[] guids = AssetDatabase.FindAssets("t:AutomatedTestScriptableObject", new[] { ValidatorConstants.Tests.TestDefinitionsPath });
            ValidationTestScriptableObject[] tests = new ValidationTestScriptableObject[guids.Length];
            for (int i = 0; i < tests.Length; i++)
            {
                string testPath = AssetDatabase.GUIDToAssetPath(guids[i]);
                AutomatedTestScriptableObject test = AssetDatabase.LoadAssetAtPath<AutomatedTestScriptableObject>(testPath);

                tests[i] = test;
            }

            switch (sortType)
            {
                default:
                case SortType.Id:
                    tests = tests.Where(x => x != null).OrderBy(x => x.Id).ToArray();
                    break;
                case SortType.Alphabetical:
                    tests = tests.Where(x => x != null).OrderBy(x => x.Title).ToArray();
                    break;
            }

            return tests;
        }

        public static MonoScript GenerateTestScript(string testName, ValidationType validationType)
        {
            var derivedType = nameof(ITestScript);
            var configType = string.Empty;
            var scriptPath = string.Empty;
            switch (validationType)
            {
                case ValidationType.Generic:
                    configType = nameof(GenericTestConfig);
                    scriptPath = ValidatorConstants.Tests.GenericTestMethodsPath;
                    break;
                case ValidationType.UnityPackage:
                    configType = nameof(GenericTestConfig);
                    scriptPath = ValidatorConstants.Tests.UnityPackageTestMethodsPath;
                    break;
                default:
                    throw new System.Exception("Undefined validation type");
            }

            var newScriptPath = $"{scriptPath}/{testName}";
            if (!newScriptPath.EndsWith(".cs"))
                newScriptPath += ".cs";

            var existingScript = AssetDatabase.LoadAssetAtPath<MonoScript>(newScriptPath);
            if (existingScript != null)
                return existingScript;

            var scriptContent =
                $"using AssetStoreTools.Validator.Data;\n" +
                $"using AssetStoreTools.Validator.TestDefinitions;\n\n" +
                $"namespace AssetStoreTools.Validator.TestMethods\n" +
                $"{{\n" +
                $"    internal class {testName} : {derivedType}\n" +
                $"    {{\n" +
                $"        private {configType} _config;\n\n" +
                $"        // Constructor also accepts dependency injection of registered {nameof(IValidatorService)} types\n" +
                $"        public {testName}({configType} config)\n" +
                $"        {{\n" +
                $"            _config = config;\n" +
                $"        }}\n\n" +
                $"        public {nameof(TestResult)} {nameof(ITestScript.Run)}()\n" +
                $"        {{\n" +
                $"            var result = new {nameof(TestResult)}() {{ {nameof(TestResult.Status)} = {nameof(TestResultStatus)}.{nameof(TestResultStatus.Undefined)} }};\n" +
                $"            return result;\n" +
                $"        }}\n" +
                $"    }}\n" +
                $"}}\n";

            File.WriteAllText(newScriptPath, scriptContent);
            AssetDatabase.Refresh();
            return AssetDatabase.LoadAssetAtPath<MonoScript>(newScriptPath);
        }

        public static string GetLongestProjectPath()
        {
            var longPaths = GetProjectPaths(new string[] { "Assets", "Packages" });
            return longPaths.Aggregate("", (max, cur) => max.Length > cur.Length ? max : cur);
        }

        public static IEnumerable<string> GetProjectPaths(string[] rootPaths)
        {
            var longPaths = new List<string>();
            var guids = AssetDatabase.FindAssets("*", rootPaths);

            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                longPaths.Add(path);
            }

            return longPaths;
        }

        public static Texture GetStatusTexture(TestResultStatus status)
        {
            var iconTheme = "";
            if (!EditorGUIUtility.isProSkin)
                iconTheme = "_d";

            switch (status)
            {
                case TestResultStatus.Pass:
                    return (Texture)EditorGUIUtility.Load($"{WindowStyles.ValidatorIconsPath}/success{iconTheme}.png");
                case TestResultStatus.Warning:
                    return (Texture)EditorGUIUtility.Load($"{WindowStyles.ValidatorIconsPath}/warning{iconTheme}.png");
                case TestResultStatus.Fail:
                    return (Texture)EditorGUIUtility.Load($"{WindowStyles.ValidatorIconsPath}/error{iconTheme}.png");
                default:
                    return (Texture)EditorGUIUtility.Load($"{WindowStyles.ValidatorIconsPath}/undefined{iconTheme}.png");
            }
        }
    }
}