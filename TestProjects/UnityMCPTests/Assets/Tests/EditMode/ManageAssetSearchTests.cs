using System;
using System.IO;
using System.Linq;
using MCPForUnity.Editor.Helpers;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MCPForUnityTests.Editor
{
    [TestFixture]
    public class ManageAssetSearchTests
    {
        private string _folder;
        private string[] _assetPaths;

        [SetUp]
        public void SetUp()
        {
            if (!AssetDatabase.IsValidFolder("Assets/Temp"))
                AssetDatabase.CreateFolder("Assets", "Temp");
            string name = "AssetSearch_" + Guid.NewGuid().ToString("N");
            AssetDatabase.CreateFolder("Assets/Temp", name);
            _folder = "Assets/Temp/" + name;
            _assetPaths = Enumerable.Range(0, 8).Select(i => _folder + "/SearchFixture" + i + ".txt").ToArray();
            foreach (string path in _assetPaths)
            {
                File.WriteAllText(path, "Search fixture");
                AssetDatabase.ImportAsset(path, ImportAssetOptions.ForceSynchronousImport);
                var asset = AssetDatabase.LoadAssetAtPath<TextAsset>(path);
                UnityEngine.Resources.UnloadAsset(asset);
                Assert.IsFalse(AssetDatabase.IsMainAssetAtPathLoaded(path), "Fixture must start unloaded");
            }
        }

        [TearDown]
        public void TearDown()
        {
            if (!string.IsNullOrEmpty(_folder)) AssetDatabase.DeleteAsset(_folder);
        }

        [Test]
        public void Search_LoadsOnlyRequestedPageWhileCountingAllMatches()
        {
            var data = Search(pageSize: 2, pageNumber: 2);
            Assert.AreEqual(8, (int)data["totalAssets"]);
            Assert.AreEqual(2, ((JArray)data["assets"]).Count);
            Assert.AreEqual(2, _assetPaths.Count(AssetDatabase.IsMainAssetAtPathLoaded),
                "Assets outside the requested page must not be loaded to build their metadata");
            foreach (var asset in data["assets"])
                Assert.IsTrue(AssetDatabase.IsMainAssetAtPathLoaded((string)asset["path"]));
        }

        [Test]
        public void Search_LastPageReturnsOnlyRemainingAssets()
        {
            var data = Search(pageSize: 3, pageNumber: 3);
            Assert.AreEqual(8, (int)data["totalAssets"]);
            Assert.AreEqual(2, ((JArray)data["assets"]).Count);
            Assert.AreEqual(2, _assetPaths.Count(AssetDatabase.IsMainAssetAtPathLoaded));
        }

        [Test]
        public void Search_PageBeyondEndDoesNotLoadAnyAssetsOrOverflow()
        {
            var data = Search(pageSize: int.MaxValue, pageNumber: int.MaxValue);
            Assert.AreEqual(8, (int)data["totalAssets"]);
            Assert.IsEmpty((JArray)data["assets"]);
            Assert.AreEqual(0, _assetPaths.Count(AssetDatabase.IsMainAssetAtPathLoaded));
        }

        [Test]
        public void Search_DateFilterCountsBeforePagingAndLoadsOnlyPage()
        {
            foreach (string path in _assetPaths.Take(4))
                File.SetLastWriteTimeUtc(path, new DateTime(2000, 1, 1, 0, 0, 0, DateTimeKind.Utc));
            var data = Search(pageSize: 2, pageNumber: 1, filterDateAfter: "2020-01-01T00:00:00Z");
            Assert.AreEqual(4, (int)data["totalAssets"]);
            Assert.AreEqual(2, ((JArray)data["assets"]).Count);
            Assert.AreEqual(2, _assetPaths.Count(AssetDatabase.IsMainAssetAtPathLoaded));
        }

        [TestCase(0, 1)]
        [TestCase(-1, 1)]
        [TestCase(2, 0)]
        [TestCase(2, -1)]
        public void Search_InvalidPagingReturnsErrorWithoutLoadingAssets(int pageSize, int pageNumber)
        {
            var response = ManageAsset.HandleCommand(SearchParams(pageSize, pageNumber));
            Assert.IsInstanceOf<ErrorResponse>(response);
            Assert.AreEqual(0, _assetPaths.Count(AssetDatabase.IsMainAssetAtPathLoaded));
        }

        [Test]
        public void Search_InvalidScopeDoesNotExpandToTheEntireProject()
        {
            var parameters = SearchParams(2, 1);
            parameters["path"] = _folder + "/MissingFolder";
            var response = ManageAsset.HandleCommand(parameters);
            Assert.IsInstanceOf<ErrorResponse>(response);
            StringAssert.Contains("searchPattern", ((ErrorResponse)response).Error);
            Assert.AreEqual(0, _assetPaths.Count(AssetDatabase.IsMainAssetAtPathLoaded));
        }

        private JObject Search(int pageSize, int pageNumber, string filterDateAfter = null)
        {
            var parameters = SearchParams(pageSize, pageNumber);
            if (filterDateAfter != null) parameters["filterDateAfter"] = filterDateAfter;
            var response = ManageAsset.HandleCommand(parameters);
            Assert.IsInstanceOf<SuccessResponse>(response);
            return JObject.FromObject(((SuccessResponse)response).Data);
        }

        private JObject SearchParams(int pageSize, int pageNumber) => new JObject
        {
            ["action"] = "search",
            ["path"] = _folder,
            ["filterType"] = "TextAsset",
            ["pageSize"] = pageSize,
            ["pageNumber"] = pageNumber,
            ["generatePreview"] = false
        };
    }
}
