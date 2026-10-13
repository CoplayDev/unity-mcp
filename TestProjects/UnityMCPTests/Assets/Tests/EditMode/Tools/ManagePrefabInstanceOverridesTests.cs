using System.Linq;
using System;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using MCPForUnity.Editor.Tools.Prefabs;
using MCPForUnity.Runtime.Helpers;
using static MCPForUnityTests.Editor.TestUtilities;
using Object = UnityEngine.Object;
using TestNamespace;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManagePrefabInstanceOverridesTests
    {
        private const string TempDirectory = "Assets/Temp/ManagePrefabInstanceOverridesTests";
        private string PrefabPath => TempDirectory + "/OverrideTarget.prefab";
        private GameObject _instance;

        [SetUp]
        public void SetUp()
        {
            StageUtility.GoToMainStage();
            EnsureFolder(TempDirectory);
            var source = new GameObject("OverrideRoot");
            source.AddComponent<Rigidbody>().mass = 1;
            source.AddComponent<BoxCollider>();
            var child = new GameObject("Child", typeof(RectTransform));
            child.transform.SetParent(source.transform, false);
            ((RectTransform)child.transform).anchorMin = new Vector2(0.1f, 0.2f);
            var removable = new GameObject("ToRemove");
            removable.transform.SetParent(source.transform, false);
            PrefabUtility.SaveAsPrefabAsset(source, PrefabPath);
            Object.DestroyImmediate(source);
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
        }

        [TearDown]
        public void TearDown()
        {
            if (_instance != null) Object.DestroyImmediate(_instance);
            AssetDatabase.DeleteAsset(TempDirectory);
            CleanupEmptyParentFolders(TempDirectory);
        }

        private JObject Command(string action, params (string Key, object Value)[] options)
        {
            var args = new JObject { ["action"] = action, ["target"] = _instance.GetInstanceIDCompat().ToString() };
            foreach (var option in options) args[option.Key] = JToken.FromObject(option.Value);
            return ToJObject(ManagePrefabs.HandleCommand(args));
        }

        private static void SetFloat(Object target, string path, float value)
        {
            using (var serialized = new SerializedObject(target))
            {
                serialized.FindProperty(path).floatValue = value;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
        }

        [Test]
        public void ListOverrides_ShowsInstanceAndPrefabValues()
        {
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);

            var response = Command("list_overrides");

            Assert.IsTrue(response.Value<bool>("success"), response.ToString());
            var group = response["data"]["propertyOverrides"].Single(item => item.Value<string>("componentType") == "UnityEngine.Rigidbody");
            var mass = group["modifications"].Single(item => item.Value<string>("propertyPath") == "m_Mass");
            Assert.AreEqual(7, mass.Value<float>("instanceValue"));
            Assert.AreEqual(1, mass.Value<float>("prefabValue"));
            Assert.IsTrue(mass.Value<bool>("valueAvailable"));
            Assert.AreEqual(7, _instance.GetComponent<Rigidbody>().mass, "Listing must not mutate the scene.");
        }

        private void AssertSuccess(JObject response)
        {
            Assert.IsTrue(response.Value<bool>("success"), response.ToString());
        }

        [TestCase(true, 0)]
        [TestCase(true, 2)]
        [TestCase(false, 0)]
        [TestCase(false, 2)]
        public void PartialFailureResponse_ReportsCompletedEntriesAndFailedOverride(bool applying, int changed)
        {
            var writtenPaths = applying && changed > 0 ? new[] { PrefabPath, PrefabPath } : Array.Empty<string>();

            var response = ToJObject(ManagePrefabInstanceOverrides.BuildPartialFailureResponse(
                applying, changed, 3, "property", "Child", 7, writtenPaths, "Override write failed."));

            Assert.IsFalse(response.Value<bool>("success"));
            var data = response["data"];
            Assert.AreEqual(changed, data.Value<int>("changed"));
            Assert.AreEqual(3, data.Value<int>("total"));
            Assert.AreEqual(changed, data.Value<int>("failedIndex"));
            Assert.AreEqual("property", data.Value<string>("overrideType"));
            Assert.AreEqual("Child", data.Value<string>("childPath"));
            Assert.AreEqual(7, data.Value<int>("overrideIndex"));
            CollectionAssert.AreEqual(writtenPaths.Distinct().ToArray(), data["prefabPaths"].Values<string>().ToArray());
            string error = response.Value<string>("error");
            StringAssert.Contains($"{changed} of 3 overrides were {(applying ? "applied" : "reverted")}", error);
            StringAssert.Contains("overrideType 'property', childPath 'Child', overrideIndex 7", error);
            StringAssert.Contains("Override write failed.", error);
            StringAssert.Contains(applying
                ? "Changes already written to prefab assets were NOT rolled back."
                : "Overrides already reverted on the instance were NOT rolled back.", error);
        }

        [Test]
        public void ListOverrides_FiltersChildComponentAndPropertyPrefix()
        {
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);
            var rect = _instance.transform.Find("Child").GetComponent<RectTransform>();
            SetFloat(rect, "m_AnchorMin.x", 0.6f);

            var response = Command("list_overrides", ("childPath", "Child"),
                ("componentType", "RectTransform"), ("propertyPrefix", "m_AnchorMin"));

            AssertSuccess(response);
            Assert.AreEqual(1, response["data"]["propertyOverrides"].Count());
            var group = response["data"]["propertyOverrides"].Single();
            Assert.AreEqual("Child", group.Value<string>("childPath"));
            Assert.IsTrue(group["modifications"].All(p => p.Value<string>("propertyPath").StartsWith("m_AnchorMin")));
            var x = group["modifications"].Single(p => p.Value<string>("propertyPath") == "m_AnchorMin.x");
            Assert.AreEqual(0.6f, x.Value<float>("instanceValue"));
            Assert.AreEqual(0.1f, x.Value<float>("prefabValue"));
        }

        [Test]
        public void RevertProperty_PreservesOtherPropertiesAndChildren()
        {
            var body = _instance.GetComponent<Rigidbody>();
            SetFloat(body, "m_Mass", 7);
            using (var serialized = new SerializedObject(body))
            {
                serialized.FindProperty("m_UseGravity").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            var rect = _instance.transform.Find("Child").GetComponent<RectTransform>();
            SetFloat(rect, "m_AnchorMin.x", 0.6f);

            AssertSuccess(Command("revert_overrides", ("componentType", "Rigidbody"), ("propertyPath", "m_Mass")));

            Assert.AreEqual(1, body.mass);
            using (var serialized = new SerializedObject(body))
                Assert.IsFalse(serialized.FindProperty("m_UseGravity").boolValue);
            Assert.AreEqual(0.6f, rect.anchorMin.x);
            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<Rigidbody>().mass);
        }

        [Test]
        public void RevertObject_DoesNotRevertDescendantsOrDefaultPlacement()
        {
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);
            SetFloat(_instance.transform, "m_LocalPosition.x", 10);
            var rect = _instance.transform.Find("Child").GetComponent<RectTransform>();
            SetFloat(rect, "m_AnchorMin.x", 0.6f);

            AssertSuccess(Command("revert_overrides"));

            Assert.AreEqual(1, _instance.GetComponent<Rigidbody>().mass);
            Assert.AreEqual(10, _instance.transform.localPosition.x);
            Assert.AreEqual(0.6f, rect.anchorMin.x);
        }

        [Test]
        public void ApplyProperty_PersistsSourceAndPreservesUnselectedOverrides()
        {
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);
            var rect = _instance.transform.Find("Child").GetComponent<RectTransform>();
            SetFloat(rect, "m_AnchorMin.x", 0.6f);

            AssertSuccess(Command("apply_overrides", ("componentType", "Rigidbody"), ("propertyPath", "m_Mass")));

            Assert.AreEqual(7, AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<Rigidbody>().mass);
            Assert.AreEqual(0.1f, AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).transform.Find("Child").GetComponent<RectTransform>().anchorMin.x);
            Assert.AreEqual(0.6f, rect.anchorMin.x);
            var remaining = Command("list_overrides", ("componentType", "Rigidbody"));
            AssertSuccess(remaining);
            Assert.AreEqual(0, remaining["data"]["propertyOverrides"].Count());
        }

        [Test]
        public void ApplyChildProperty_DoesNotApplyRootOverrides()
        {
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);
            SetFloat(_instance.transform.Find("Child").GetComponent<RectTransform>(), "m_AnchorMin.x", 0.6f);

            AssertSuccess(Command("apply_overrides", ("childPath", "Child"), ("componentType", "RectTransform"), ("propertyPath", "m_AnchorMin.x")));

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.AreEqual(0.6f, asset.transform.Find("Child").GetComponent<RectTransform>().anchorMin.x);
            Assert.AreEqual(1, asset.GetComponent<Rigidbody>().mass);
            Assert.AreEqual(7, _instance.GetComponent<Rigidbody>().mass);
        }

        [Test]
        public void ListOverrides_ReportsEveryStructuralKind()
        {
            _instance.AddComponent<AudioSource>();
            Object.DestroyImmediate(_instance.GetComponent<BoxCollider>());
            new GameObject("Extra").transform.SetParent(_instance.transform, false);
            Object.DestroyImmediate(_instance.transform.Find("ToRemove").gameObject);

            var response = Command("list_overrides");

            AssertSuccess(response);
            foreach (string key in new[] { "addedComponents", "removedComponents", "addedGameObjects" })
                Assert.AreEqual(1, response["data"][key].Count(), key);
            if (response["data"].Value<bool>("removedGameObjectsSupported"))
            {
                Assert.AreEqual(1, response["data"]["removedGameObjects"].Count());
                Assert.AreEqual("ToRemove", response["data"]["removedGameObjects"].Single().Value<string>("childPath"));
            }
        }

        private string PrepareStructuralOverride(string kind)
        {
            switch (kind)
            {
                case "added_component": _instance.AddComponent<AudioSource>(); return "";
                case "removed_component": Object.DestroyImmediate(_instance.GetComponent<BoxCollider>()); return "";
                case "added_gameobject": new GameObject("Extra").transform.SetParent(_instance.transform, false); return "Extra";
                case "removed_gameobject": Object.DestroyImmediate(_instance.transform.Find("ToRemove").gameObject); return "ToRemove";
                default: throw new ArgumentException(kind);
            }
        }

        [TestCase("added_component")]
        [TestCase("removed_component")]
        [TestCase("added_gameobject")]
        [TestCase("removed_gameobject")]
        public void RevertStructuralOverride_ChangesOnlySelectedKind(string kind)
        {
            string path = PrepareStructuralOverride(kind);
            if (kind == "removed_gameobject" && !Command("list_overrides")["data"].Value<bool>("removedGameObjectsSupported"))
                Assert.Ignore("This Unity version has no removed GameObject override API.");
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);

            AssertSuccess(Command("revert_overrides", ("childPath", path), ("overrideType", kind), ("overrideIndex", 0)));

            Assert.IsNull(_instance.GetComponent<AudioSource>());
            Assert.IsNotNull(_instance.GetComponent<BoxCollider>());
            Assert.IsNull(_instance.transform.Find("Extra"));
            Assert.IsNotNull(_instance.transform.Find("ToRemove"));
            Assert.AreEqual(7, _instance.GetComponent<Rigidbody>().mass);
        }

        [TestCase("added_component")]
        [TestCase("removed_component")]
        [TestCase("added_gameobject")]
        [TestCase("removed_gameobject")]
        public void ApplyStructuralOverride_UpdatesAssetAndPreservesPropertyOverride(string kind)
        {
            string path = PrepareStructuralOverride(kind);
            if (kind == "removed_gameobject" && !Command("list_overrides")["data"].Value<bool>("removedGameObjectsSupported"))
                Assert.Ignore("This Unity version has no removed GameObject override API.");
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);

            AssertSuccess(Command("apply_overrides", ("childPath", path), ("overrideType", kind), ("overrideIndex", 0)));

            var asset = AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath);
            Assert.AreEqual(kind == "added_component", asset.GetComponent<AudioSource>() != null);
            Assert.AreEqual(kind != "removed_component", asset.GetComponent<BoxCollider>() != null);
            Assert.AreEqual(kind == "added_gameobject", asset.transform.Find("Extra") != null);
            Assert.AreEqual(kind != "removed_gameobject", asset.transform.Find("ToRemove") != null);
            Assert.AreEqual(1, asset.GetComponent<Rigidbody>().mass);
            Assert.AreEqual(7, _instance.GetComponent<Rigidbody>().mass);
        }

        [Test]
        public void AmbiguousStructuralSelection_IsRejectedWithoutMutation()
        {
            var first = _instance.AddComponent<BoxCollider>();
            var second = _instance.AddComponent<BoxCollider>();
            var response = Command("revert_overrides", ("overrideType", "added_component"), ("componentType", "BoxCollider"));
            Assert.IsFalse(response.Value<bool>("success"));
            Assert.IsNotNull(first);
            Assert.IsNotNull(second);

            var listed = Command("list_overrides", ("overrideType", "added_component"));
            int index = listed["data"]["addedComponents"].First().Value<int>("overrideIndex");
            AssertSuccess(Command("revert_overrides", ("overrideType", "added_component"), ("overrideIndex", index)));
            Assert.AreEqual(2, _instance.GetComponents<BoxCollider>().Length);
        }

        [TestCase("../Child")]
        [TestCase("/Child")]
        [TestCase("Missing")]
        public void InvalidChildPath_IsRejected(string path)
        {
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);
            var response = Command("revert_overrides", ("childPath", path));
            Assert.IsFalse(response.Value<bool>("success"));
            Assert.AreEqual(7, _instance.GetComponent<Rigidbody>().mass);
        }

        [Test]
        public void InvalidPrefabDestination_IsRejectedWithoutMutation()
        {
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);
            var response = Command("apply_overrides", ("prefabPath", TempDirectory + "/Missing.prefab"));
            Assert.IsFalse(response.Value<bool>("success"));
            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<Rigidbody>().mass);
            Assert.AreEqual(7, _instance.GetComponent<Rigidbody>().mass);
        }

        [Test]
        public void DefaultsAndPrefix_CannotBroadenWrites()
        {
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);
            Assert.IsFalse(Command("revert_overrides", ("includeDefaultOverrides", true)).Value<bool>("success"));
            Assert.IsFalse(Command("apply_overrides", ("propertyPrefix", "m_")).Value<bool>("success"));
            Assert.AreEqual(7, _instance.GetComponent<Rigidbody>().mass);
        }

        [Test]
        public void NonPrefabTarget_IsRejected()
        {
            var plain = new GameObject("PlainTarget");
            try
            {
                var args = new JObject { ["action"] = "list_overrides", ["target"] = plain.GetInstanceIDCompat().ToString() };
                Assert.IsFalse(ToJObject(ManagePrefabs.HandleCommand(args)).Value<bool>("success"));
            }
            finally { Object.DestroyImmediate(plain); }
        }

        [Test]
        public void ExplicitDefaults_RevertOnlyRequestedPlacementProperty()
        {
            SetFloat(_instance.transform, "m_LocalPosition.x", 10);
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);
            var excluded = Command("list_overrides", ("componentType", "Transform"), ("propertyPath", "m_LocalPosition.x"));
            AssertSuccess(excluded);
            Assert.AreEqual(0, excluded["data"]["propertyOverrides"].Count());
            var included = Command("list_overrides", ("componentType", "Transform"), ("propertyPath", "m_LocalPosition.x"), ("includeDefaultOverrides", true));
            AssertSuccess(included);
            Assert.AreEqual(10, included["data"]["propertyOverrides"].Single()["modifications"].Single().Value<float>("instanceValue"));

            AssertSuccess(Command("revert_overrides", ("componentType", "Transform"), ("propertyPath", "m_LocalPosition.x"), ("includeDefaultOverrides", true)));

            Assert.AreEqual(0, _instance.transform.localPosition.x);
            Assert.AreEqual(7, _instance.GetComponent<Rigidbody>().mass);
        }

        [Test]
        public void InstanceId_SelectsOnlyOneOfDuplicateChildNames()
        {
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                Object.Instantiate(contents.transform.Find("Child").gameObject, contents.transform).name = "Child";
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            Object.DestroyImmediate(_instance);
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var children = _instance.GetComponentsInChildren<RectTransform>();
            SetFloat(children[0], "m_AnchorMin.x", 0.6f);
            SetFloat(children[1], "m_AnchorMin.x", 0.8f);
            Assert.IsFalse(Command("revert_overrides", ("childPath", "Child")).Value<bool>("success"));
            var args = new JObject { ["action"] = "revert_overrides", ["target"] = children[0].gameObject.GetInstanceIDCompat().ToString() };

            AssertSuccess(ToJObject(ManagePrefabs.HandleCommand(args)));

            Assert.AreEqual(0.1f, children[0].anchorMin.x);
            Assert.AreEqual(0.8f, children[1].anchorMin.x);
        }

        [Test]
        public void AddedChild_CanBeTargetedDirectlyByInstanceId()
        {
            var extra = new GameObject("Extra");
            extra.transform.SetParent(_instance.transform, false);
            var args = new JObject { ["action"] = "revert_overrides", ["target"] = extra.GetInstanceIDCompat().ToString(),
                ["overrideType"] = "added_gameobject" };

            AssertSuccess(ToJObject(ManagePrefabs.HandleCommand(args)));

            Assert.IsTrue(extra == null);
            Assert.IsNotNull(_instance);
        }

        [Test]
        public void Variant_ListsInheritedValueAndAppliesToVariantOnly()
        {
            string variantPath = TempDirectory + "/Variant.prefab";
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 3);
            PrefabUtility.SaveAsPrefabAsset(_instance, variantPath);
            Object.DestroyImmediate(_instance);
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(variantPath));
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);
            var listed = Command("list_overrides", ("componentType", "Rigidbody"), ("propertyPath", "m_Mass"));
            AssertSuccess(listed);
            Assert.AreEqual(3, listed["data"]["propertyOverrides"].Single()["modifications"].Single().Value<float>("prefabValue"));

            AssertSuccess(Command("apply_overrides", ("componentType", "Rigidbody"), ("propertyPath", "m_Mass")));

            Assert.AreEqual(7, AssetDatabase.LoadAssetAtPath<GameObject>(variantPath).GetComponent<Rigidbody>().mass);
            Assert.AreEqual(1, AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<Rigidbody>().mass);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void NestedPrefabs_SelectOneInstanceAndAllowAnExplicitSource(bool applyToInnerSource)
        {
            string outerPath = TempDirectory + "/Outer.prefab";
            var outer = new GameObject("Outer");
            for (int i = 0; i < 2; i++)
            {
                var nested = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
                nested.name = "Nested" + i;
                nested.transform.SetParent(outer.transform, false);
            }
            PrefabUtility.SaveAsPrefabAsset(outer, outerPath);
            Object.DestroyImmediate(outer);
            Object.DestroyImmediate(_instance);
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(outerPath));
            var first = _instance.transform.Find("Nested0").GetComponent<Rigidbody>();
            var second = _instance.transform.Find("Nested1").GetComponent<Rigidbody>();
            SetFloat(first, "m_Mass", 7);
            SetFloat(second, "m_Mass", 9);
            var listed = Command("list_overrides", ("childPath", "Nested0"), ("propertyPath", "m_Mass"));
            AssertSuccess(listed);
            Assert.AreEqual(1, listed["data"]["propertyOverrides"].Count());
            Assert.AreEqual(1, listed["data"]["propertyOverrides"].Single()["modifications"].Single().Value<float>("prefabValue"));

            var applied = applyToInnerSource
                ? Command("apply_overrides", ("childPath", "Nested0"), ("componentType", "Rigidbody"), ("propertyPath", "m_Mass"), ("prefabPath", PrefabPath))
                : Command("apply_overrides", ("childPath", "Nested0"), ("componentType", "Rigidbody"), ("propertyPath", "m_Mass"));
            AssertSuccess(applied);

            Assert.AreEqual(applyToInnerSource ? 7 : 1, AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<Rigidbody>().mass);
            var outerAsset = AssetDatabase.LoadAssetAtPath<GameObject>(outerPath);
            Assert.AreEqual(7, outerAsset.transform.Find("Nested0").GetComponent<Rigidbody>().mass);
            Assert.AreEqual(applyToInnerSource ? 7 : 1, outerAsset.transform.Find("Nested1").GetComponent<Rigidbody>().mass);
            Assert.AreEqual(9, second.mass);
        }

        [Test]
        public void SceneOnlyObjectReference_IsListedButCannotBeApplied()
        {
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                contents.AddComponent<MeshFilter>();
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            Object.DestroyImmediate(_instance);
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            var mesh = new Mesh { name = "UnsavedMesh" };
            try
            {
                using (var serialized = new SerializedObject(_instance.GetComponent<MeshFilter>()))
                {
                    serialized.FindProperty("m_Mesh").objectReferenceValue = mesh;
                    serialized.ApplyModifiedPropertiesWithoutUndo();
                }
                var listed = Command("list_overrides", ("componentType", "MeshFilter"), ("propertyPath", "m_Mesh"));
                AssertSuccess(listed);
                var property = listed["data"]["propertyOverrides"].Single()["modifications"].Single();
                Assert.AreEqual("UnsavedMesh", property["instanceValue"].Value<string>("name"));
                Assert.AreEqual(JTokenType.Null, property["prefabValue"].Type);
                Assert.IsTrue(property.Value<bool>("valueAvailable"));

                Assert.IsFalse(Command("apply_overrides", ("componentType", "MeshFilter"), ("propertyPath", "m_Mesh")).Value<bool>("success"));

                Assert.IsNull(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<MeshFilter>().sharedMesh);
                Assert.AreSame(mesh, _instance.GetComponent<MeshFilter>().sharedMesh);
            }
            finally { Object.DestroyImmediate(mesh); }
        }

        private PrefabOverrideTestComponent AddReferenceFixture()
        {
            var contents = PrefabUtility.LoadPrefabContents(PrefabPath);
            try
            {
                var component = contents.AddComponent<PrefabOverrideTestComponent>();
                component.node = new PrefabOverrideTestComponent.Node { value = 2 };
                component.node.next = component.node;
                PrefabUtility.SaveAsPrefabAsset(contents, PrefabPath);
            }
            finally { PrefabUtility.UnloadPrefabContents(contents); }
            Object.DestroyImmediate(_instance);
            _instance = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath));
            return _instance.GetComponent<PrefabOverrideTestComponent>();
        }

        [Test]
        public void CyclicManagedReferences_DoNotBlockListingOrApplyingAnotherProperty()
        {
            var component = AddReferenceFixture();
            SetFloat(component, "count", 7);

            var listed = Command("list_overrides", ("componentType", "PrefabOverrideTestComponent"));
            AssertSuccess(listed);
            Assert.AreEqual(7, listed["data"]["propertyOverrides"].Single()["modifications"].Single(p => p.Value<string>("propertyPath") == "count").Value<float>("instanceValue"));
            AssertSuccess(Command("apply_overrides", ("componentType", "PrefabOverrideTestComponent"), ("propertyPath", "count")));

            Assert.AreEqual(7, AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<PrefabOverrideTestComponent>().count);
            Assert.AreSame(component.node, component.node.next);
        }

        [Test]
        public void MissingArrayElement_CannotImplicitlyApplyTheWholeArray()
        {
            var component = AddReferenceFixture();
            using (var serialized = new SerializedObject(component))
            {
                var array = serialized.FindProperty("values");
                array.arraySize = 2;
                array.GetArrayElementAtIndex(0).floatValue = 7;
                array.GetArrayElementAtIndex(1).floatValue = 9;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }

            var response = Command("apply_overrides", ("componentType", "PrefabOverrideTestComponent"), ("propertyPath", "values.Array.data[1]"));

            Assert.IsFalse(response.Value<bool>("success"));
            CollectionAssert.AreEqual(new float[] { 1 }, AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<PrefabOverrideTestComponent>().values);
            CollectionAssert.AreEqual(new float[] { 7, 9 }, component.values);

            AssertSuccess(Command("apply_overrides", ("componentType", "PrefabOverrideTestComponent"), ("propertyPath", "values")));
            CollectionAssert.AreEqual(new float[] { 7, 9 }, AssetDatabase.LoadAssetAtPath<GameObject>(PrefabPath).GetComponent<PrefabOverrideTestComponent>().values);
        }

        [Test]
        public void PropertyIndex_IsStableAcrossDefaultOverrideFilters()
        {
            SetFloat(_instance.transform, "m_LocalPosition.x", 10);
            SetFloat(_instance.GetComponent<Rigidbody>(), "m_Mass", 7);
            var rect = _instance.transform.Find("Child").GetComponent<RectTransform>();
            SetFloat(rect, "m_AnchorMin.x", 0.6f);
            var listed = Command("list_overrides", ("includeDefaultOverrides", true));
            AssertSuccess(listed);
            int index = listed["data"]["propertyOverrides"].Single(group => group.Value<string>("componentType") == "UnityEngine.Rigidbody").Value<int>("overrideIndex");

            AssertSuccess(Command("revert_overrides", ("overrideType", "property"), ("overrideIndex", index)));

            Assert.AreEqual(1, _instance.GetComponent<Rigidbody>().mass);
            Assert.AreEqual(0.6f, rect.anchorMin.x);
            Assert.AreEqual(10, _instance.transform.localPosition.x);
        }
    }
}
