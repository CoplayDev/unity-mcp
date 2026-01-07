using AssetStoreTools.Validator.Data;
using Newtonsoft.Json;
using Newtonsoft.Json.Serialization;
using System.Collections.Generic;
using System.Reflection;

namespace AssetStoreTools.Uploader.Services.Analytics.Data
{
    internal class ValidationResultsSerializer
    {
        private class ValidationResults
        {
            public bool HasCompilationErrors;
            public string[] Paths;
            public Dictionary<string, TestResultOutcome> Results;
        }

        private class TestResultOutcome
        {
            public int IntegerValue;
            public string StringValue;

            public TestResultOutcome(TestResultStatus status)
            {
                IntegerValue = (int)status;
                StringValue = status.ToString();
            }
        }

        private class ValidationResultsResolver : DefaultContractResolver
        {
            private static ValidationResultsResolver _instance;
            public static ValidationResultsResolver Instance => _instance ?? (_instance = new ValidationResultsResolver());

            private Dictionary<string, string> _propertyConversion;

            private ValidationResultsResolver()
            {
                _propertyConversion = new Dictionary<string, string>()
                {
                    { nameof(ValidationResults.HasCompilationErrors), "has_compilation_errors" },
                    { nameof(ValidationResults.Paths), "validation_paths" },
                    { nameof(ValidationResults.Results), "validation_results" },
                    { nameof(TestResultOutcome.IntegerValue), "int" },
                    { nameof(TestResultOutcome.StringValue), "string" },
                };
            }

            protected override JsonProperty CreateProperty(MemberInfo member, MemberSerialization memberSerialization)
            {
                var property = base.CreateProperty(member, memberSerialization);
                if (_propertyConversion.ContainsKey(property.PropertyName))
                    property.PropertyName = _propertyConversion[property.PropertyName];

                return property;
            }
        }

        public static string ConstructValidationResultsJson(ValidationSettings settings, ValidationResult result)
        {
            if (result == null)
                return string.Empty;

            var resultObject = new ValidationResults();
            resultObject.HasCompilationErrors = result.HadCompilationErrors;

            switch (settings)
            {
                case CurrentProjectValidationSettings currentProjectValidationSettings:
                    resultObject.Paths = currentProjectValidationSettings.ValidationPaths.ToArray();
                    break;
                case ExternalProjectValidationSettings externalProjectValidationSettings:
                    resultObject.Paths = new string[] { externalProjectValidationSettings.PackagePath };
                    break;
            }

            resultObject.Results = new Dictionary<string, TestResultOutcome>();
            foreach (var test in result.Tests)
            {
                resultObject.Results.Add(test.Id.ToString(), new TestResultOutcome(test.Result.Status));
            }

            var serializerSettings = new JsonSerializerSettings()
            {
                ContractResolver = ValidationResultsResolver.Instance
            };

            return JsonConvert.SerializeObject(resultObject, serializerSettings);
        }
    }
}