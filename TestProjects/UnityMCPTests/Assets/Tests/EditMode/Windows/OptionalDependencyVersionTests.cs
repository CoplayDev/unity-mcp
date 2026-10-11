using System;
using System.Reflection;
using MCPForUnity.Editor.Dependencies;
using MCPForUnity.Editor.Windows;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;

namespace MCPForUnityTests.Editor.Windows
{
    public class OptionalDependencyTestWindow : EditorWindow { }

    public class OptionalDependencyVersionTests
    {
        private OptionalDependencyTestWindow window;
        private PackageVersionSelection selection;

        [TearDown]
        public void Cleanup()
        {
            if (selection != null) SessionState.EraseString(selection.SessionKey);
            if (window != null) window.Close();
        }

        [Test]
        public void DependenciesOfferVersionInput_ForEveryOptionalUpmPackage()
        {
            var container = new VisualElement();
            typeof(MCPForUnityEditorWindow).GetMethod("BuildDependenciesSection", BindingFlags.NonPublic | BindingFlags.Static)
                .Invoke(null, new object[] { container });
            foreach (var package in new[] { "com.unity.probuilder", "com.unity.cinemachine", "com.unity.visualeffectgraph", "com.unity.cloud.gltfast" })
                Assert.IsNotNull(container.Q<TextField>(package + "-version"), package + " has no version input.");
        }

        [TestCase("2.4.0")]
        [TestCase("3.0.0-pre.1")]
        [TestCase("1.2.3+build.04")]
        [TestCase("0.0.0")]
        [TestCase(" 2.10.5 ")]
        public void MissingPackage_RequestsExactSelectedVersion(string version)
        {
            var entry = new PackageVersionSelection("com.unity.cinemachine", null, false) { Version = version };
            Assert.IsTrue(entry.TryGetRequest(out var request, out var error), error);
            Assert.AreEqual("com.unity.cinemachine@" + version.Trim(), request);
        }

        [TestCase("2.4")]
        [TestCase("latest")]
        [TestCase("1.2.*")]
        [TestCase("file:somewhere")]
        [TestCase("com.other@2.4.0")]
        [TestCase("01.2.3")]
        [TestCase("1.2.3-01")]
        [TestCase("1.2.3?")]
        [TestCase("1.2.\n3")]
        [TestCase("١.2.3")]
        public void InvalidVersion_DoesNotProduceAnInstallRequest(string version)
        {
            var entry = new PackageVersionSelection("com.unity.cinemachine", null, false) { Version = version };
            Assert.IsFalse(entry.TryGetRequest(out var request, out var error));
            Assert.IsNull(request);
            StringAssert.Contains("exact package version", error);
        }

        [TestCase("")]
        [TestCase("2.4.0")]
        [TestCase(" 2.4.0 ")]
        public void ExistingPackage_DefaultOrBlank_DoesNotRewriteVersionOrSource(string version)
        {
            var entry = new PackageVersionSelection("com.unity.cinemachine", "2.4.0", true) { Version = version };
            Assert.IsTrue(entry.TryGetRequest(out var request, out var error), error);
            Assert.IsNull(request);
        }

        [Test]
        public void ExistingPackage_ExplicitChange_RequestsSelectedVersion()
        {
            var entry = new PackageVersionSelection("com.unity.cinemachine", "3.1.0", true) { Version = "2.4.0" };
            Assert.IsTrue(entry.TryGetRequest(out var request, out _));
            Assert.AreEqual("com.unity.cinemachine@2.4.0", request);
        }

        [Test]
        public void MissingPackage_Blank_UsesPackageManagerAutomaticCompatibility()
        {
            var entry = new PackageVersionSelection("com.unity.cinemachine", null, false);
            Assert.IsTrue(entry.TryGetRequest(out var request, out _));
            Assert.AreEqual("com.unity.cinemachine", request);
        }

        [Test]
        public void BulkSelection_PreservesInstalledAndUsesPinnedOrAutomaticMissingVersions()
        {
            var entries = new[] {
                new PackageVersionSelection("com.unity.cinemachine", "2.4.0", true),
                new PackageVersionSelection("com.unity.probuilder", null, false) { Version = "5.2.3" },
                new PackageVersionSelection("com.unity.cloud.gltfast", null, false)
            };
            Assert.IsTrue(PackageVersionSelection.TryCollectRequests(entries, out var requests, out var error), error);
            CollectionAssert.AreEqual(new[] { "com.unity.probuilder@5.2.3", "com.unity.cloud.gltfast" }, requests);
            entries[0].Version = "3.1.0";
            Assert.IsTrue(PackageVersionSelection.TryCollectRequests(entries, out requests, out error), error);
            Assert.AreEqual("com.unity.cinemachine@3.1.0", requests[0]);
        }

        [Test]
        public void BulkInvalidVersion_ReturnsNoPartialRequests()
        {
            var entries = new[] {
                new PackageVersionSelection("com.unity.probuilder", null, false) { Version = "5.2.3" },
                new PackageVersionSelection("com.unity.cinemachine", null, false) { Version = "bad" }
            };
            Assert.IsFalse(PackageVersionSelection.TryCollectRequests(entries, out var requests, out var error));
            Assert.IsEmpty(requests);
            StringAssert.StartsWith("com.unity.cinemachine:", error);
        }

        private VisualElement MakeRow(bool installed, string installedVersion, Action<Action> install)
        {
            window = ScriptableObject.CreateInstance<OptionalDependencyTestWindow>();
            window.Show();
            selection = new PackageVersionSelection("com.example.fixture." + Guid.NewGuid().ToString("N"), installedVersion, installed);
            return MCPForUnityEditorWindow.AddDependencyRow(window.rootVisualElement, "Test package", "A versioned dependency",
                installed, "Installed", "Not installed", install, null, selection);
        }

        private static void Click(Button button)
        {
            Assert.IsTrue(button.enabledInHierarchy);
            var invoke = typeof(Clickable).GetMethod("Invoke", BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.IsNotNull(invoke, "Clickable invocation is unavailable.");
            invoke.Invoke(button.clickable, new object[] { null });
        }

        [Test]
        public void VersionField_UpdatesIndividualAndBulkRequests_AndRecoversFromInvalidInput()
        {
            string installedRequest = null;
            Action complete = null;
            var row = MakeRow(true, "3.1.0", done => {
                Assert.IsTrue(selection.TryGetRequest(out installedRequest, out _));
                complete = done;
            });
            var field = row.Q<TextField>();
            var button = row.Q<Button>(selection.PackageId + "-install");
            var message = row.Q<Label>(selection.PackageId + "-version-message");
            Assert.AreEqual("3.1.0", field.value);
            Assert.AreEqual("Change Version", button.text);
            Assert.IsFalse(button.enabledSelf);
            field.value = "bad";
            Assert.IsFalse(button.enabledSelf);
            StringAssert.Contains("exact package version", message.text);
            field.value = "2.4.0";
            Assert.IsTrue(button.enabledSelf);
            StringAssert.Contains("3.1.0 to 2.4.0", message.text);
            Assert.IsTrue(PackageVersionSelection.TryCollectRequests(new[] { selection }, out var bulk, out _));
            Click(button);
            Assert.AreEqual(selection.PackageId + "@2.4.0", installedRequest);
            Assert.AreEqual(installedRequest, bulk[0]);
            Assert.AreEqual("2.4.0", SessionState.GetString(selection.SessionKey, ""));
            Assert.IsFalse(field.enabledInHierarchy, "Version must be stable while its install is pending.");
            complete();
            Assert.IsTrue(field.enabledInHierarchy);
            Assert.IsTrue(button.enabledSelf);
        }

        [Test]
        public void MissingPackageUi_ExplainsAutomaticSelection_AndAllowsPinnedVersion()
        {
            var row = MakeRow(false, null, done => done());
            var field = row.Q<TextField>();
            var button = row.Q<Button>();
            var message = row.Q<Label>(selection.PackageId + "-version-message");
            Assert.AreEqual("Install", button.text);
            Assert.IsTrue(button.enabledSelf);
            StringAssert.Contains("latest compatible", message.text);
            field.value = "2.4.0";
            StringAssert.Contains("Install version 2.4.0", message.text);
            field.value = "";
            StringAssert.Contains("latest compatible", message.text);
        }

        [Test]
        public void ExternalDependency_IsPreservedEvenWithStaleSelection_AndCannotAddDuplicateUpmPackage()
        {
            var row = MakeRow(true, null, done => Assert.Fail("External dependency must not install a duplicate package."));
            selection.Version = "2.4.0";
            Assert.IsFalse(row.Q<TextField>().enabledSelf);
            Assert.IsFalse(row.Q<Button>().enabledSelf);
            StringAssert.Contains("original source", row.Q<Label>(selection.PackageId + "-version-message").text);
            Assert.IsTrue(PackageVersionSelection.TryCollectRequests(new[] { selection }, out var requests, out _));
            Assert.IsEmpty(requests);
        }
    }
}
