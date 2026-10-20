using System;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools.Cameras;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using PackageInfo = UnityEditor.PackageManager.PackageInfo;

namespace MCPForUnityTests.Editor.Tools
{
    public class CinemachineVersionTests
    {
        private static JObject Ping() => JObject.FromObject(
            ManageCamera.HandleCommand(new JObject { ["action"] = "ping" }), UnityJsonSerializer.Instance);

        [Test]
        public void Ping_ReportsInstalledUpmRelease_SeparatelyFromNativeApiSupport()
        {
            var package = PackageInfo.GetAllRegisteredPackages().FirstOrDefault(p => p.name == "com.unity.cinemachine");
            var response = Ping();
            Assert.IsTrue(response.Value<bool>("success"));
            Assert.AreEqual(3, response["data"].Value<int>("supportedApiMajor"));
            Assert.AreEqual(CameraHelpers.HasCinemachine, response["data"].Value<bool>("cinemachine"));
            if (package != null)
            {
                Assert.IsTrue(response["data"].Value<bool>("installed"));
                Assert.AreEqual(package.version, response["data"].Value<string>("version"));
            }
            else if (!CameraHelpers.IsCinemachineInstalled)
            {
                Assert.IsFalse(response["data"].Value<bool>("installed"));
                Assert.IsNull(response["data"].Value<string>("version"));
                StringAssert.Contains("not installed", response.Value<string>("message"));
            }
        }

        [Test]
        public void Cinemachine2_PingExplainsInstalledVersion_AndPreservesApiDistinction()
        {
            RequireCinemachine2();
            var response = Ping();
            Assert.IsTrue(response["data"].Value<bool>("installed"));
            Assert.IsFalse(response["data"].Value<bool>("cinemachine"));
            StringAssert.StartsWith("2.", response["data"].Value<string>("version"));
            StringAssert.Contains("Cinemachine 3 camera API", response.Value<string>("message"));
            StringAssert.Contains("execute_code", response.Value<string>("message"));
            StringAssert.DoesNotContain("not installed", response.Value<string>("message"));
        }

        [TestCase("ensure_brain")]
        [TestCase("force_camera")]
        public void Cinemachine2_UnsupportedAction_UsesInstalledApiGuidance(string action)
        {
            RequireCinemachine2();
            var response = JObject.FromObject(ManageCamera.HandleCommand(new JObject { ["action"] = action }), UnityJsonSerializer.Instance);
            Assert.IsFalse(response.Value<bool>("success"));
            StringAssert.Contains("Cinemachine 3 camera API", response.Value<string>("error"));
            StringAssert.Contains("Keep the project's installed version", response.Value<string>("error"));
            StringAssert.Contains("get_package_info", response.Value<string>("error"));
            StringAssert.DoesNotContain("Install Cinemachine", response.Value<string>("error"));
            StringAssert.DoesNotContain("not installed", response.Value<string>("error"));
        }

        [Test]
        public void Cinemachine2_BasicCameraFallback_DoesNotSuggestReplacingInstalledPackage()
        {
            RequireCinemachine2();
            string name = "Cinemachine2Fallback_" + Guid.NewGuid().ToString("N");
            UnityEngine.GameObject camera = null;
            try
            {
                var response = JObject.FromObject(ManageCamera.HandleCommand(new JObject {
                    ["action"] = "create_camera", ["properties"] = new JObject { ["name"] = name }
                }), UnityJsonSerializer.Instance);
                camera = UnityEngine.GameObject.Find(name);
                Assert.IsTrue(response.Value<bool>("success"));
                Assert.IsNotNull(camera);
                Assert.IsNotNull(camera.GetComponent<UnityEngine.Camera>());
                StringAssert.Contains("Cinemachine 3 camera API", response.Value<string>("message"));
                StringAssert.DoesNotContain("not installed", response.Value<string>("message"));
                StringAssert.Contains("get_package_info", response["data"].Value<string>("hint"));
                StringAssert.DoesNotContain("Install com.unity.cinemachine", response["data"].Value<string>("hint"));
            }
            finally
            {
                if (camera == null) camera = UnityEngine.GameObject.Find(name);
                if (camera != null) UnityEngine.Object.DestroyImmediate(camera);
            }
        }

        private static void RequireCinemachine2()
        {
            if (!CameraHelpers.GetCinemachineVersion()?.StartsWith("2.", StringComparison.Ordinal) ?? true)
                Assert.Ignore("Requires an isolated project with Cinemachine 2 installed.");
        }
    }
}
