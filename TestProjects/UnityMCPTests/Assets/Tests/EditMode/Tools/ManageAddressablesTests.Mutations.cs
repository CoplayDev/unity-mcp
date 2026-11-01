using System;
using System.Collections;
using System.IO;
using System.Linq;
using System.Reflection;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace MCPForUnityTests.Editor.Tools
{
    public partial class ManageAddressablesTests
    {
        private JObject Hero() => Query("entry_find", new JObject { ["path"] = Root + "/Hero.txt" });

        private string ConfigurationSnapshot()
        {
            var groups = ((IEnumerable)_settings.GetType().GetProperty("groups").GetValue(_settings)).Cast<UnityEngine.Object>();
            return string.Join("\n", new[] { (UnityEngine.Object)_settings }.Concat(groups)
                .Select(asset =>
                {
                    var data = JObject.Parse(EditorJsonUtility.ToJson(asset));
                    // Native lookups lazily compute this derived cache; compare authored values and all disk bytes.
                    data.Descendants().OfType<JProperty>().Where(property => string.Equals(property.Name, "m_currentHash", StringComparison.OrdinalIgnoreCase))
                        .ToList().ForEach(property => property.Remove());
                    return data.ToString() + "\n" + File.ReadAllText(AssetDatabase.GetAssetPath(asset));
                }));
        }

        [TestCase("group_create", "{'name':'../Escape'}")]
        [TestCase("group_create", "{'name':'A'}")]
        [TestCase("group_remove", "{'group':'A','force':'yes'}")]
        [TestCase("entry_add", "{'path':'Assets/../outside.txt','group':'B'}")]
        [TestCase("entry_add", "{'path':'$hero','group':'B'}")]
        [TestCase("entry_move", "{'path':'$hero','group':'missing'}")]
        [TestCase("entry_set_address", "{'path':'$hero','address':'[invalid]'}")]
        [TestCase("label_set", "{'path':'$hero','labels':['combat','missing']}")]
        [TestCase("label_set", "{'path':'$hero','labels':['combat',7]}")]
        [TestCase("label_remove", "{'name':'combat'}")]
        [TestCase("profile_set", "{'profile_id':'missing','name':'Remote.LoadPath','value':'changed'}")]
        [TestCase("profile_set_active", "{'profile_id':'missing'}")]
        [TestCase("set_settings", "{'settings':{'unique_bundle_ids':true,'remote_catalog_load_path':'missing'}}")]
        public void RejectedMutations_PreserveMemoryDiskAndSourceAssets(string action, string json)
        {
            CreateSettings();
            AddEntry("Hero", Group("A"));
            Group("B");
            Succeeded(Query("label_add", new JObject { ["name"] = "combat" }));
            Succeeded(Query("label_set", new JObject { ["path"] = Root + "/Hero.txt", ["labels"] = new JArray("combat") }));
            string before = ConfigurationSnapshot();
            string[] paths = AssetDatabase.GetAllAssetPaths();
            var result = Query(action, JObject.Parse(json.Replace("$hero", Root + "/Hero.txt")));
            Assert.That(result["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"), result.ToString());
            Assert.That(ConfigurationSnapshot(), Is.EqualTo(before));
            CollectionAssert.AreEquivalent(paths, AssetDatabase.GetAllAssetPaths());
            Assert.That(File.ReadAllText(Root + "/Hero.txt"), Is.EqualTo("Hero"));
        }

        [TestCase("entry_add")]
        [TestCase("entry_move")]
        [TestCase("group_remove")]
        public void ReadOnlyTargetGroup_RejectsWritesWithoutChangingEntries(string action)
        {
            CreateSettings();
            AddEntry("Hero", Group("A"));
            var target = (UnityEngine.Object)Group("B");
            using (var serialized = new SerializedObject(target))
            {
                serialized.FindProperty("m_ReadOnly").boolValue = true;
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.SaveAssets();
            File.WriteAllText(Root + "/New.txt", "new");
            AssetDatabase.ImportAsset(Root + "/New.txt");
            string before = ConfigurationSnapshot();
            var args = new JObject { ["group"] = "B", ["force"] = true,
                ["path"] = Root + (action == "entry_add" ? "/New.txt" : "/Hero.txt") };
            Assert.That(Query(action, args)["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(ConfigurationSnapshot(), Is.EqualTo(before));
        }

        [Test]
        public void EntryAdd_ConfigAssetIsRejectedBeforeRegistration()
        {
            CreateSettings();
            Group("A");
            AssetDatabase.SaveAssets();
            string before = ConfigurationSnapshot();
            var result = Query("entry_add", new JObject { ["group"] = "A", ["path"] = AssetDatabase.GetAssetPath((UnityEngine.Object)_settings) });
            Assert.That(result["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(ConfigurationSnapshot(), Is.EqualTo(before));
            Assert.That(Query("entry_list")["data"]["total"].Value<int>(), Is.Zero);
        }

        [TestCase("entry_add")]
        [TestCase("entry_move")]
        [TestCase("entry_remove")]
        public void EntryMutations_UndoRedoRestoreRegistrationAndGroup(string action)
        {
            CreateSettings();
            var source = Group("A");
            Group("B");
            if (action == "entry_add")
            {
                File.WriteAllText(Root + "/Hero.txt", "Hero");
                AssetDatabase.ImportAsset(Root + "/Hero.txt");
            }
            else AddEntry("Hero", source);
            AssetDatabase.SaveAssets();
            var before = Hero();
            Succeeded(Query(action, new JObject { ["path"] = Root + "/Hero.txt", ["group"] = action == "entry_move" ? "B" : "A" }));
            var changed = Hero();
            Undo.PerformUndo();
            Assert.That(Hero().ToString(), Is.EqualTo(before.ToString()));
            Undo.PerformRedo();
            Assert.That(Hero().ToString(), Is.EqualTo(changed.ToString()));
            Assert.That(File.Exists(Root + "/Hero.txt"), Is.True);
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Labels_UndoRedoRestoresDefinitionsAndAssignments(bool removeDefinition)
        {
            CreateSettings();
            AddEntry("Hero", Group("A"));
            Succeeded(Query("label_add", new JObject { ["name"] = "combat" }));
            if (removeDefinition)
                Succeeded(Query("label_set", new JObject { ["path"] = Root + "/Hero.txt", ["labels"] = new JArray("combat") }));
            var before = new JArray(Hero(), Query("label_list"));
            if (removeDefinition) Succeeded(Query("label_remove", new JObject { ["name"] = "combat", ["force"] = true }));
            else Succeeded(Query("label_set", new JObject { ["path"] = Root + "/Hero.txt", ["labels"] = new JArray("combat") }));
            var changed = new JArray(Hero(), Query("label_list"));
            Undo.PerformUndo();
            Assert.That(new JArray(Hero(), Query("label_list")).ToString(), Is.EqualTo(before.ToString()));
            Undo.PerformRedo();
            Assert.That(new JArray(Hero(), Query("label_list")).ToString(), Is.EqualTo(changed.ToString()));
        }

        [Test]
        public void EntryAdd_AfterUndoRegistersAgainWithoutUsingStaleNativeCache()
        {
            CreateSettings();
            Group("A");
            Group("B");
            File.WriteAllText(Root + "/Hero.txt", "Hero");
            AssetDatabase.ImportAsset(Root + "/Hero.txt");
            Succeeded(Query("entry_add", new JObject { ["path"] = Root + "/Hero.txt", ["group"] = "A" }));
            Assert.That(Hero()["data"]["found"].Value<bool>(), Is.True);
            Undo.PerformUndo();
            Assert.That(Hero()["data"]["found"].Value<bool>(), Is.False);
            Succeeded(Query("entry_add", new JObject { ["path"] = Root + "/Hero.txt", ["group"] = "B" }));
            Assert.That(Query("entry_list", new JObject { ["group"] = "A" })["data"]["total"].Value<int>(), Is.Zero);
            Assert.That(Query("entry_list", new JObject { ["group"] = "B" })["data"]["total"].Value<int>(), Is.EqualTo(1));
        }

        [TestCase(false)]
        [TestCase(true)]
        public void Profiles_UndoRedoRestoresVariableOrActiveSelection(bool activate)
        {
            CreateSettings();
            var profiles = _settings.GetType().GetProperty("profileSettings").GetValue(_settings);
            string originalId = Query("get_settings")["data"]["active_profile_id"].Value<string>();
            string id = (string)profiles.GetType().GetMethod("AddProfile").Invoke(profiles, new object[] { "Other", originalId });
            AssetDatabase.SaveAssets();
            var before = new JArray(Query("profile_get", new JObject { ["profile_id"] = id }), Query("get_settings"));
            Succeeded(Query(activate ? "profile_set_active" : "profile_set", new JObject
                { ["profile_id"] = id, ["name"] = "Remote.LoadPath", ["value"] = "" }));
            var changed = new JArray(Query("profile_get", new JObject { ["profile_id"] = id }), Query("get_settings"));
            Undo.PerformUndo();
            Assert.That(new JArray(Query("profile_get", new JObject { ["profile_id"] = id }), Query("get_settings")).ToString(), Is.EqualTo(before.ToString()));
            Undo.PerformRedo();
            Assert.That(new JArray(Query("profile_get", new JObject { ["profile_id"] = id }), Query("get_settings")).ToString(), Is.EqualTo(changed.ToString()));
        }

        [Test]
        public void SavedConfiguration_SurvivesUnloadAndReloadFromDisk()
        {
            CreateSettings();
            Group("A");
            File.WriteAllText(Root + "/Hero.txt", "Hero");
            AssetDatabase.ImportAsset(Root + "/Hero.txt");
            Succeeded(Query("entry_add", new JObject { ["group"] = "A", ["path"] = Root + "/Hero.txt" }));
            Succeeded(Query("entry_set_address", new JObject { ["path"] = Root + "/Hero.txt", ["address"] = "saved/hero" }));
            Succeeded(Query("label_add", new JObject { ["name"] = "persisted" }));
            Succeeded(Query("label_set", new JObject { ["path"] = Root + "/Hero.txt", ["labels"] = new JArray("persisted") }));
            Succeeded(Query("set_settings", new JObject { ["settings"] = new JObject { ["unique_bundle_ids"] = true } }));
            string profile = Query("get_settings")["data"]["active_profile_id"].Value<string>();
            Succeeded(Query("profile_set", new JObject { ["profile_id"] = profile, ["name"] = "Remote.LoadPath", ["value"] = "https://example.invalid/saved" }));
            var groups = ((IEnumerable)_settings.GetType().GetProperty("groups").GetValue(_settings)).Cast<UnityEngine.Object>().ToArray();
            string path = AssetDatabase.GetAssetPath((UnityEngine.Object)_settings);
            _defaultType.GetField("s_DefaultSettingsObject", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            foreach (var group in groups) UnityEngine.Resources.UnloadAsset(group);
            UnityEngine.Resources.UnloadAsset((UnityEngine.Object)_settings);
            _settings = AssetDatabase.LoadAssetAtPath(path, _defaultType.Assembly.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings"));
            Assert.That(Hero()["data"]["entry"]["address"].Value<string>(), Is.EqualTo("saved/hero"));
            Assert.That(Hero()["data"]["entry"]["labels"].Values<string>(), Is.EquivalentTo(new[] { "persisted" }));
            Assert.That(Query("get_settings")["data"]["values"]["unique_bundle_ids"].Value<bool>(), Is.True);
            Assert.That(Query("profile_get")["data"]["items"].Single(row => row["name"].Value<string>() == "Remote.LoadPath")["value"].Value<string>(), Is.EqualTo("https://example.invalid/saved"));
        }
    }
}
