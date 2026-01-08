using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityObject = UnityEngine.Object;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckTypeNamespaces : ITestScript
    {
        private readonly string[] ForbiddenNamespaces = new string[] { "Unity" };

        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;
        private IScriptUtilityService _scriptUtility;

        private enum NamespaceEligibility
        {
            NoNamespace,
            Ok,
            Forbidden
        }

        private class AnalysisResult
        {
            public Dictionary<UnityObject, List<string>> TypesWithoutNamespaces;
            public Dictionary<UnityObject, List<string>> ForbiddenNamespaces;

            public bool HasIssues => TypesWithoutNamespaces.Count > 0
                || ForbiddenNamespaces.Count > 0;
        }

        public CheckTypeNamespaces(GenericTestConfig config, IAssetUtilityService assetUtility, IScriptUtilityService scriptUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
            _scriptUtility = scriptUtility;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var scriptResult = CheckScripts();
            var assemblyResult = CheckAssemblies();

            if (scriptResult.HasIssues || assemblyResult.HasIssues)
            {
                result.Status = TestResultStatus.Warning;

                // Error conditions for forbidden namespaces

                if (scriptResult.ForbiddenNamespaces.Count > 0)
                {
                    result.Status = TestResultStatus.Fail;
                    result.AddMessage("The following scripts contain namespaces starting with a 'Unity' keyword:");
                    AddJoinedMessage(result, scriptResult.ForbiddenNamespaces);
                }

                if (assemblyResult.ForbiddenNamespaces.Count > 0)
                {
                    result.Status = TestResultStatus.Fail;
                    result.AddMessage("The following assemblies contain namespaces starting with a 'Unity' keyword:");
                    AddJoinedMessage(result, assemblyResult.ForbiddenNamespaces);
                }

                // Variable severity conditions for no-namespace types

                if (scriptResult.TypesWithoutNamespaces.Count > 0)
                {
                    if (result.Status != TestResultStatus.Fail)
                        result.Status = TestResultStatus.VariableSeverityIssue;

                    result.AddMessage("The following scripts contain types not nested under a namespace:");
                    AddJoinedMessage(result, scriptResult.TypesWithoutNamespaces);
                }

                if (assemblyResult.TypesWithoutNamespaces.Count > 0)
                {
                    if (result.Status != TestResultStatus.Fail)
                        result.Status = TestResultStatus.VariableSeverityIssue;

                    result.AddMessage("The following assemblies contain types not nested under a namespace:");
                    AddJoinedMessage(result, assemblyResult.TypesWithoutNamespaces);
                }
            }
            else
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("All scripts contain valid namespaces!");
            }

            return result;
        }

        private AnalysisResult CheckScripts()
        {
            var scripts = _assetUtility.GetObjectsFromAssets<MonoScript>(_config.ValidationPaths, AssetType.MonoScript).ToArray();
            var scriptNamespaces = _scriptUtility.GetTypeNamespacesFromScriptAssets(scripts);

            var scriptsWithoutNamespaces = new Dictionary<UnityObject, List<string>>();
            var scriptsWithForbiddenNamespaces = new Dictionary<UnityObject, List<string>>();

            foreach (var kvp in scriptNamespaces)
            {
                var scriptAsset = kvp.Key;
                var typesInScriptAsset = kvp.Value;

                var typesWithoutNamespace = new List<string>();
                var discouragedNamespaces = new List<string>();
                var forbiddenNamespaces = new List<string>();

                foreach (var t in typesInScriptAsset)
                {
                    var eligibility = CheckNamespaceEligibility(t.Namespace);

                    switch (eligibility)
                    {
                        case NamespaceEligibility.NoNamespace:
                            typesWithoutNamespace.Add(t.Name);
                            break;
                        case NamespaceEligibility.Forbidden:
                            if (!forbiddenNamespaces.Contains(t.Namespace))
                                forbiddenNamespaces.Add(t.Namespace);
                            break;
                        case NamespaceEligibility.Ok:
                            break;
                    }
                }

                if (typesWithoutNamespace.Count > 0)
                    scriptsWithoutNamespaces.Add(scriptAsset, typesWithoutNamespace);

                if (forbiddenNamespaces.Count > 0)
                    scriptsWithForbiddenNamespaces.Add(scriptAsset, forbiddenNamespaces);
            }

            return new AnalysisResult
            {
                TypesWithoutNamespaces = scriptsWithoutNamespaces,
                ForbiddenNamespaces = scriptsWithForbiddenNamespaces
            };
        }

        private AnalysisResult CheckAssemblies()
        {
            var assemblies = _assetUtility.GetObjectsFromAssets(_config.ValidationPaths, AssetType.PrecompiledAssembly).ToList();
            var assemblyTypes = _scriptUtility.GetTypesFromAssemblies(assemblies);

            var assembliesWithoutNamespaces = new Dictionary<UnityObject, List<string>>();
            var assembliesWithForbiddenNamespaces = new Dictionary<UnityObject, List<string>>();

            foreach (var kvp in assemblyTypes)
            {
                var assemblyAsset = kvp.Key;
                var typesInAssembly = kvp.Value;

                var typesWithoutNamespace = new List<string>();
                var discouragedNamespaces = new List<string>();
                var forbiddenNamespaces = new List<string>();

                foreach (var t in typesInAssembly)
                {
                    var eligibility = CheckNamespaceEligibility(t.Namespace);

                    switch (eligibility)
                    {
                        case NamespaceEligibility.NoNamespace:
                            typesWithoutNamespace.Add($"{GetTypeName(t)} {t.Name}");
                            break;
                        case NamespaceEligibility.Forbidden:
                            if (!forbiddenNamespaces.Contains(t.Namespace))
                                forbiddenNamespaces.Add(t.Namespace);
                            break;
                        case NamespaceEligibility.Ok:
                            break;
                    }
                }

                if (typesWithoutNamespace.Count > 0)
                    assembliesWithoutNamespaces.Add(assemblyAsset, typesWithoutNamespace);

                if (forbiddenNamespaces.Count > 0)
                    assembliesWithForbiddenNamespaces.Add(assemblyAsset, forbiddenNamespaces);
            }

            return new AnalysisResult
            {
                TypesWithoutNamespaces = assembliesWithoutNamespaces,
                ForbiddenNamespaces = assembliesWithForbiddenNamespaces
            };
        }

        private NamespaceEligibility CheckNamespaceEligibility(string fullNamespace)
        {
            if (string.IsNullOrEmpty(fullNamespace))
                return NamespaceEligibility.NoNamespace;

            var split = fullNamespace.Split('.');
            var topLevelNamespace = split[0];
            if (ForbiddenNamespaces.Any(x => topLevelNamespace.StartsWith(x, StringComparison.OrdinalIgnoreCase)))
                return NamespaceEligibility.Forbidden;

            return NamespaceEligibility.Ok;
        }

        private string GetTypeName(Type type)
        {
            if (type.IsClass)
                return "class";
            if (type.IsInterface)
                return "interface";
            if (type.IsEnum)
                return "enum";
            if (type.IsValueType)
                return "struct";

            throw new ArgumentException($"Received an unrecognizable type {type}. Type must be either a class, interface, struct or enum");
        }

        private void AddJoinedMessage(TestResult result, Dictionary<UnityObject, List<string>> assetsWithMessages)
        {
            foreach (var kvp in assetsWithMessages)
            {
                var message = string.Join("\n", kvp.Value);
                result.AddMessage(message, null, kvp.Key);
            }
        }
    }
}
