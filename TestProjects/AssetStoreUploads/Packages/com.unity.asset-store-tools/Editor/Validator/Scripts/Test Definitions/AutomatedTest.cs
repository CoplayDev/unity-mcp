using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace AssetStoreTools.Validator.TestDefinitions
{
    internal class AutomatedTest : ValidationTest
    {
        public AutomatedTest(ValidationTestScriptableObject source) : base(source) { }

        public override void Run(ITestConfig config)
        {
            Type testClass = null;
            MethodInfo testMethod = null;

            try
            {
                ValidateTestMethod(ref testClass, ref testMethod);
                ValidateConfig(config);
            }
            catch (Exception e)
            {
                Debug.LogError(e.Message);
                return;
            }

            object testClassInstance;
            try
            {
                testClassInstance = CreateInstance(testClass, config);
            }
            catch (Exception e)
            {
                Debug.LogError($"Could not create an instance of class {testClass}:\n{e}");
                return;
            }

            try
            {
                Result = (TestResult)testMethod.Invoke(testClassInstance, new object[0]);
            }
            catch (Exception e)
            {
                var result = new TestResult() { Status = TestResultStatus.Undefined };
                result.AddMessage("An exception was caught when running this test case. See Console for more details");
                Debug.LogError($"An exception was caught when running validation for test case '{Title}'\n{e}");
                Result = result;
            }
        }

        private void ValidateTestMethod(ref Type testClass, ref MethodInfo testMethod)
        {
            if (TestScript == null || (testClass = TestScript.GetClass()) == null)
                throw new Exception($"Cannot run test {Title} - Test Script class was not found");

            var interfaces = testClass.GetInterfaces();
            if (!interfaces.Contains(typeof(ITestScript)))
                throw new Exception($"Cannot run test {Title} - Test Script class is not derived from {nameof(ITestScript)}");

            testMethod = testClass.GetMethod("Run");
            if (testMethod == null)
                throw new Exception($"Cannot run test {Title} - Run() method was not found");
        }

        private void ValidateConfig(ITestConfig config)
        {
            switch (ValidationType)
            {
                case ValidationType.Generic:
                case ValidationType.UnityPackage:
                    if (config is GenericTestConfig)
                        return;
                    break;
                default:
                    throw new NotImplementedException("Undefined validation type");
            }

            throw new Exception("Config does not match the validation type");
        }

        private object CreateInstance(Type testClass, ITestConfig testConfig)
        {
            var constructors = testClass.GetConstructors();
            if (constructors.Length != 1)
                throw new Exception($"Test class {testClass} should only contain a single constructor");

            var constructor = constructors[0];
            var expectedParameters = constructor.GetParameters();
            var parametersToUse = new List<object>();
            foreach (var expectedParam in expectedParameters)
            {
                var paramType = expectedParam.ParameterType;

                if (paramType == testConfig.GetType())
                {
                    parametersToUse.Add(testConfig);
                    continue;
                }

                if (typeof(IValidatorService).IsAssignableFrom(paramType))
                {
                    var matchingService = ValidatorServiceProvider.Instance.GetService(paramType);
                    if (matchingService == null)
                        throw new Exception($"Service {paramType} is not registered and could not be retrieved");

                    parametersToUse.Add(matchingService);
                    continue;
                }

                throw new Exception($"Invalid parameter type: {paramType}");
            }

            var instance = constructor.Invoke(parametersToUse.ToArray());
            return instance;
        }
    }
}