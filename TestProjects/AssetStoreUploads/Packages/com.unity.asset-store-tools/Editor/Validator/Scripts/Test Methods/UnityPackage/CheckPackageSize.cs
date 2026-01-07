using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.TestDefinitions;
using System.IO;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckPackageSize : ITestScript
    {
        private GenericTestConfig _config;

        public CheckPackageSize(GenericTestConfig config)
        {
            _config = config;
        }

        public TestResult Run()
        {
            var result = new TestResult() { Status = TestResultStatus.Undefined };

            var packageSize = CalculatePackageSize(_config.ValidationPaths);
            float packageSizeInGB = packageSize / (1024f * 1024f * 1024f);
            float maxPackageSizeInGB = Constants.Uploader.MaxPackageSizeBytes / (1024f * 1024f * 1024f);

            if (packageSizeInGB - maxPackageSizeInGB >= 0.1f)
            {
                result.Status = TestResultStatus.Warning;

                result.AddMessage($"The uncompressed size of your package ({packageSizeInGB:0.#} GB) exceeds the maximum allowed package size of {maxPackageSizeInGB:0.#} GB. " +
                    $"Please make sure that the compressed .unitypackage size does not exceed the size limit.");
            }
            else
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("Your package does not exceed the maximum allowed package size!");
            }

            return result;
        }

        private long CalculatePackageSize(string[] assetPaths)
        {
            long totalSize = 0;

            foreach (var path in assetPaths)
            {
                totalSize += CalculatePathSize(path);
            }

            return totalSize;
        }

        private long CalculatePathSize(string path)
        {
            long size = 0;

            var dirInfo = new DirectoryInfo(path);
            if (!dirInfo.Exists)
                return size;

            foreach (var file in dirInfo.EnumerateFiles())
                size += file.Length;

            foreach (var nestedDir in dirInfo.EnumerateDirectories())
                size += CalculatePathSize(nestedDir.FullName);

            return size;
        }
    }
}
