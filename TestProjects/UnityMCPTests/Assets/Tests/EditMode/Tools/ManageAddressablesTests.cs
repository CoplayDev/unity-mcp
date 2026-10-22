using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using MCPForUnity.Editor.Tools;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using UnityEngine.TestTools;

namespace MCPForUnityTests.Editor.Tools
{
    public partial class ManageAddressablesTests
    {
        private const string Root = "Assets/__McpAddressablesTests";
        private Type _defaultType;
        private object _oldSettings;
        private object _settings;
        private UnityEngine.Object _oldDefaultObject;
        private UnityEngine.Object _oldLegacyObject;
        private FieldInfo _postprocessorField;
        private object _oldPostprocessor;
        private bool _fixtureActive;
        private const string BuildJobKey = "MCPForUnity.Addressables.LastBuild";
        private FieldInfo _buildJobField;
        private object _oldBuildJob;
        private string _oldBuildRecord;
        private HashSet<Delegate> _oldEditorUpdates;
        private bool _buildStateCaptured;

        [SetUp]
        public void CaptureBuildState()
        {
            var jobs = typeof(ManageAddressables).Assembly.GetType("MCPForUnity.Editor.Tools.AddressablesBuildJobs");
            _buildJobField = jobs.GetField("_job", BindingFlags.NonPublic | BindingFlags.Static);
            _oldBuildJob = _buildJobField.GetValue(null);
            string state = (_oldBuildJob as JObject)?["state"]?.Value<string>();
            if (state == "queued" || state == "running") Assert.Ignore("Do not interrupt an existing Addressables content job.");
            _oldBuildRecord = SessionState.GetString(BuildJobKey, "");
            _oldEditorUpdates = new HashSet<Delegate>(EditorApplication.update?.GetInvocationList() ?? Array.Empty<Delegate>());
            _buildStateCaptured = true;
        }

        [TestCase(false)]
        [TestCase(true)]
        public void FixtureCleanup_RestoresBuildStateAndRemovesQueuedWork(bool simulateFailure)
        {
            if (Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor") == null)
                Assert.Ignore("Requires the optional Addressables package.");
            var previous = new JObject { ["job_id"] = "fixture-preservation-probe", ["state"] = "succeeded" };
            string saved = previous.ToString();
            _buildJobField.SetValue(null, previous);
            SessionState.SetString(BuildJobKey, saved);
            var fixture = new ManageAddressablesTests();
            fixture.CaptureBuildState();
            var before = EditorApplication.update?.GetInvocationList() ?? Array.Empty<Delegate>();
            try
            {
                fixture.CreateSettings();
                var queued = Query("build_clean");
                Assert.That(queued["data"]?["state"]?.Value<string>(), Is.EqualTo("queued"), queued.ToString());
                if (simulateFailure) throw new InvalidOperationException("fixture-cleanup-probe");
            }
            catch (InvalidOperationException ex) when (simulateFailure && ex.Message == "fixture-cleanup-probe")
            {
                // Exercise cleanup after an exception while a native operation is still queued.
            }
            finally
            {
                fixture.TearDown();
            }
            Assert.That(_buildJobField.GetValue(null), Is.SameAs(previous));
            Assert.That(SessionState.GetString(BuildJobKey, ""), Is.EqualTo(saved));
            Assert.That(AssetDatabase.IsValidFolder(Root), Is.False);
            var leaked = (EditorApplication.update?.GetInvocationList() ?? Array.Empty<Delegate>()).Except(before)
                .Where(callback => callback.Method.DeclaringType?.FullName?.StartsWith(
                    "MCPForUnity.Editor.Tools.AddressablesBuildJobs", StringComparison.Ordinal) == true);
            Assert.That(leaked, Is.Empty, "Fixture cleanup left a queued content operation behind.");
        }

        [Test]
        public void FixtureAssetImports_DoNotReachPreviousProjectPostprocessor()
        {
            var defaultType = Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            if (defaultType == null) Assert.Ignore("Requires the optional Addressables package.");
            var processorType = defaultType.Assembly.GetType("UnityEditor.AddressableAssets.Settings.AddressablesAssetPostProcessor");
            var field = processorType.GetField("s_OnPostProcessHandler", BindingFlags.NonPublic | BindingFlags.Static);
            var previous = field.GetValue(null);
            var register = previous.GetType().GetMethod("Register");
            int calls = 0;
            Action<string[], string[], string[], string[]> observer = (_, __, ___, ____) => calls++;
            var callback = Delegate.CreateDelegate(register.GetParameters()[0].ParameterType, observer.Target, observer.Method);
            register.Invoke(previous, new object[] { callback, 1000 });
            try
            {
                CreateSettings();
                int before = calls;
                var group = (UnityEngine.Object)Group("IsolationProbe");
                AssetDatabase.ImportAsset(AssetDatabase.GetAssetPath(group), ImportAssetOptions.ForceUpdate | ImportAssetOptions.ForceSynchronousImport);
                Assert.That(calls, Is.EqualTo(before), "Fixture group imports reached the previous project's Addressables callbacks.");
                TearDown();
                Assert.That(field.GetValue(null), Is.SameAs(previous), "The original callback registry was not restored.");
                previous.GetType().GetMethod("Invoke").Invoke(previous,
                    new object[] { Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>(), Array.Empty<string>() });
                Assert.That(calls, Is.GreaterThan(before), "Original callbacks were not restored after fixture cleanup.");
            }
            finally
            {
                TearDown();
                previous.GetType().GetMethod("Unregister").Invoke(previous, new object[] { callback });
            }
        }

        private static JObject Query(string action, JObject args = null)
        {
            args ??= new JObject();
            args["action"] = action;
            return JObject.FromObject(ManageAddressables.HandleCommand(args));
        }

        [Test]
        public void MissingAndUnknownAction_ReturnNamedFailures()
        {
            Assert.That(JObject.FromObject(ManageAddressables.HandleCommand(null))["success"].Value<bool>(), Is.False);
            Assert.That(Query("invalid")["error"].Value<string>(), Does.Contain("Unknown action"));
            Assert.That(JObject.FromObject(ManageAddressables.HandleCommand(new JObject()))["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
        }

        [Test]
        public void Ping_ReportsPackageStateWithoutCreatingAssets()
        {
            string[] before = AssetDatabase.GetAllAssetPaths();
            var result = Query("ping");
            Assert.That(result["success"].Value<bool>(), Is.True, result.ToString());
            Assert.That(result["data"]["installed"], Is.Not.Null);
            Assert.That(result["data"]["api_available"], Is.Not.Null);
            CollectionAssert.AreEquivalent(before, AssetDatabase.GetAllAssetPaths());
        }

        [Test]
        public void MissingPackage_QueryRefusesWithoutCompileDependency()
        {
            if (Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor") != null)
                Assert.Ignore("This check exercises the no-package environment.");
            Assert.That(Query("group_list")["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_NOT_INSTALLED"));
        }

        private void CreateSettings()
        {
            _defaultType = Type.GetType("UnityEditor.AddressableAssets.AddressableAssetSettingsDefaultObject, Unity.Addressables.Editor");
            if (_defaultType == null) Assert.Ignore("Requires the optional Addressables package.");
            Assert.That(AssetDatabase.IsValidFolder(Root), Is.False, "Refuse to replace an existing test folder.");
            EditorBuildSettings.TryGetConfigObject("com.unity.addressableassets", out _oldDefaultObject);
            EditorBuildSettings.TryGetConfigObject("AddressableAssetSettings", out _oldLegacyObject);
            // Preserve the cache before replacing the config; do not trigger legacy migration while setting up tests.
            _oldSettings = _defaultType.GetField("s_DefaultSettingsObject", BindingFlags.NonPublic | BindingFlags.Static).GetValue(null);
            var processorType = _defaultType.Assembly.GetType("UnityEditor.AddressableAssets.Settings.AddressablesAssetPostProcessor");
            _postprocessorField = processorType.GetField("s_OnPostProcessHandler", BindingFlags.NonPublic | BindingFlags.Static);
            _oldPostprocessor = _postprocessorField.GetValue(null);
            // Changing the default wrapper alone leaves native import callbacks bound to the project's settings.
            _postprocessorField.SetValue(null, Activator.CreateInstance(_oldPostprocessor.GetType(), true));
            _fixtureActive = true;
            EditorBuildSettings.RemoveConfigObject("com.unity.addressableassets");
            EditorBuildSettings.RemoveConfigObject("AddressableAssetSettings");
            AssetDatabase.CreateFolder("Assets", "__McpAddressablesTests");
            AssetDatabase.CreateFolder(Root, "Config");
            var settingsType = _defaultType.Assembly.GetType("UnityEditor.AddressableAssets.Settings.AddressableAssetSettings");
            _settings = settingsType.GetMethod("Create", new[] { typeof(string), typeof(string), typeof(bool), typeof(bool) })
                .Invoke(null, new object[] { Root + "/Config", "Settings", false, true });
            // Keep the temporary default wrapper under the test root; never overwrite the project's own wrapper asset.
            var wrapper = ScriptableObject.CreateInstance(_defaultType);
            using (var serialized = new SerializedObject(wrapper))
            {
                serialized.FindProperty("m_AddressableAssetSettingsGuid").stringValue =
                    AssetDatabase.AssetPathToGUID(AssetDatabase.GetAssetPath((UnityEngine.Object)_settings));
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            AssetDatabase.CreateAsset(wrapper, Root + "/DefaultObject.asset");
            EditorBuildSettings.AddConfigObject("com.unity.addressableassets", wrapper, true);
            _defaultType.GetField("s_DefaultSettingsObject", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, _settings);
            var processor = _postprocessorField.GetValue(null);
            var register = processor.GetType().GetMethod("Register");
            var handler = _settings.GetType().GetMethod("OnPostprocessAllAssets", BindingFlags.NonPublic | BindingFlags.Instance);
            var callback = Delegate.CreateDelegate(register.GetParameters()[0].ParameterType, _settings, handler);
            register.Invoke(processor, new object[] { callback, 0 });
        }

        [TearDown]
        public void TearDown()
        {
            try
            {
                if (_buildStateCaptured)
                {
                    // A failed assertion can leave Run queued; detach it before restoring the previous job record.
                    foreach (var callback in (EditorApplication.update?.GetInvocationList() ?? Array.Empty<Delegate>()).Except(_oldEditorUpdates))
                        if (callback.Method.DeclaringType?.FullName?.StartsWith(
                            "MCPForUnity.Editor.Tools.AddressablesBuildJobs", StringComparison.Ordinal) == true)
                            EditorApplication.update -= (EditorApplication.CallbackFunction)callback;
                }
                RestoreFixtureAssets();
            }
            finally
            {
                if (_buildStateCaptured)
                {
                    _buildJobField.SetValue(null, _oldBuildJob);
                    if (string.IsNullOrEmpty(_oldBuildRecord)) SessionState.EraseString(BuildJobKey);
                    else SessionState.SetString(BuildJobKey, _oldBuildRecord);
                    _buildStateCaptured = false;
                }
            }
        }

        private void RestoreFixtureAssets()
        {
            if (!_fixtureActive) return;
            try
            {
                // Keep the project's callbacks suspended until all fixture asset deletion has finished.
                _postprocessorField.SetValue(null, Activator.CreateInstance(_oldPostprocessor.GetType(), true));
                EditorBuildSettings.RemoveConfigObject("com.unity.addressableassets");
                EditorBuildSettings.RemoveConfigObject("AddressableAssetSettings");
                AssetDatabase.DeleteAsset(Root);
            }
            finally
            {
                _defaultType.GetField("s_DefaultSettingsObject", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, _oldSettings);
                if (_oldDefaultObject != null) EditorBuildSettings.AddConfigObject("com.unity.addressableassets", _oldDefaultObject, true);
                if (_oldLegacyObject != null) EditorBuildSettings.AddConfigObject("AddressableAssetSettings", _oldLegacyObject, true);
                _postprocessorField.SetValue(null, _oldPostprocessor);
                _fixtureActive = false;
                _settings = null;
            }
        }

        private object Group(string name)
        {
            var method = _settings.GetType().GetMethods().Single(m => m.Name == "CreateGroup");
            return method.Invoke(_settings, new object[] { name, false, false, true, null, Array.Empty<Type>() });
        }

        private object AddEntry(string name, object group)
        {
            string path = Root + "/" + name + ".txt";
            File.WriteAllText(path, name);
            AssetDatabase.ImportAsset(path);
            string guid = AssetDatabase.AssetPathToGUID(path);
            return _settings.GetType().GetMethods().Single(m => m.Name == "CreateOrMoveEntry")
                .Invoke(_settings, new[] { guid, group, (object)false, true });
        }

        [Test]
        public void GetSettings_WithNoGroups_DoesNotCreateDefaultGroup()
        {
            CreateSettings();
            string[] before = AssetDatabase.GetAllAssetPaths();
            var result = Query("get_settings");
            Assert.That(result["success"].Value<bool>(), Is.True, result.ToString());
            Assert.That(result["data"]["group_count"].Value<int>(), Is.Zero);
            CollectionAssert.AreEquivalent(before, AssetDatabase.GetAllAssetPaths());
        }

        [Test]
        public void NoSettings_QueryDoesNotCreateThem()
        {
            CreateSettings();
            EditorBuildSettings.RemoveConfigObject("com.unity.addressableassets");
            _defaultType.GetField("s_DefaultSettingsObject", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            string[] before = AssetDatabase.GetAllAssetPaths();
            Assert.That(Query("get_settings")["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_SETTINGS_NOT_FOUND"));
            CollectionAssert.AreEquivalent(before, AssetDatabase.GetAllAssetPaths());
        }

        [Test]
        public void Groups_PagesAreCompleteAndStaleCursorIsRejected()
        {
            CreateSettings();
            Group("Actors");
            Group("World");
            var first = Query("group_list", new JObject { ["page_size"] = 1 });
            Assert.That(first["success"].Value<bool>(), Is.True, first.ToString());
            Assert.That(first["data"]["items"].Count(), Is.EqualTo(1));
            var second = Query("group_list", new JObject { ["page_size"] = 1, ["cursor"] = first["data"]["next_cursor"] });
            Assert.That(second["data"]["items"][0]["guid"], Is.Not.EqualTo(first["data"]["items"][0]["guid"]));
            Assert.That(second["data"]["next_cursor"].Type, Is.EqualTo(JTokenType.Null));
            Group("UI");
            var stale = Query("group_list", new JObject { ["cursor"] = first["data"]["next_cursor"] });
            Assert.That(stale["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(stale["data"]["message"].Value<string>(), Does.Contain("stale cursor"));
        }

        [TestCase("0")]
        [TestCase("201")]
        [TestCase("2.7")]
        [TestCase("2147483648")]
        [TestCase("true")]
        public void PageSize_InvalidValuesReturnArgumentFailure(string value)
        {
            CreateSettings();
            var result = Query("group_list", new JObject { ["page_size"] = JToken.Parse(value) });
            Assert.That(result["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"), result.ToString());
            Assert.That(result["data"]["message"].Value<string>(), Does.Contain("page_size"));
        }

        [Test]
        public void Entries_FilterAndFindReturnRealAssetIdentityAndLabels()
        {
            CreateSettings();
            var group = Group("Actors");
            object entry = AddEntry("Hero", group);
            entry.GetType().GetProperty("address").SetValue(entry, "hero");
            entry.GetType().GetMethod("SetLabel").Invoke(entry, new object[] { "characters", true, true, true });
            var listed = Query("entry_list", new JObject { ["group"] = "Actors" });
            Assert.That(listed["success"].Value<bool>(), Is.True, listed.ToString());
            Assert.That(listed["data"]["items"][0]["address"].Value<string>(), Is.EqualTo("hero"));
            Assert.That(listed["data"]["items"][0]["labels"].Values<string>(), Does.Contain("characters"));
            var found = Query("entry_find", new JObject { ["path"] = Root + "/Hero.txt" });
            Assert.That(found["data"]["found"].Value<bool>(), Is.True, found.ToString());
            Assert.That(found["data"]["entry"]["guid"].Value<string>(), Is.EqualTo(AssetDatabase.AssetPathToGUID(Root + "/Hero.txt")));
            var upper = Query("entry_find", new JObject { ["guid"] = found["data"]["entry"]["guid"].ToString().ToUpperInvariant() });
            Assert.That(upper["data"]["found"].Value<bool>(), Is.True, upper.ToString());
            var missing = Query("entry_find", new JObject { ["path"] = Root + "/Missing.txt" });
            Assert.That(missing["data"]["found"].Value<bool>(), Is.False);
            var labels = Query("label_list");
            Assert.That(labels["data"]["items"].Any(row => row["name"].Value<string>() == "characters"), Is.True);
        }

        [Test]
        public void EntryPages_AcrossGroupsRetainEachEntryGroupIdentifier()
        {
            CreateSettings();
            var a = Group("A");
            var b = Group("B");
            Group("Empty");
            AddEntry("A1", a);
            AddEntry("A2", a);
            AddEntry("B1", b);
            AddEntry("B2", b);
            AddEntry("B3", b);
            var groups = Query("group_list")["data"]["items"].ToDictionary(row => row["name"].Value<string>(), row => row["guid"].Value<string>());
            var seen = new HashSet<string>();
            string cursor = null;
            do
            {
                var args = new JObject { ["page_size"] = 2 };
                if (cursor != null) args["cursor"] = cursor;
                var page = Query("entry_list", args);
                Succeeded(page);
                Assert.That(page["data"]["total"].Value<int>(), Is.EqualTo(5));
                Assert.That(page["data"]["items"].Count(), Is.InRange(1, 2));
                foreach (var entry in page["data"]["items"])
                {
                    string path = entry["path"].Value<string>();
                    string groupName = Path.GetFileNameWithoutExtension(path).Substring(0, 1);
                    Assert.That(seen.Add(entry["guid"].Value<string>()), Is.True, "Repeated entry across pages.");
                    Assert.That(entry["group_guid"].Value<string>(), Is.EqualTo(groups[groupName]));
                    Assert.That(JToken.DeepEquals(Query("entry_find", new JObject { ["path"] = path })["data"]["entry"], entry), Is.True);
                }
                cursor = page["data"]["next_cursor"].Value<string>();
            } while (cursor != null);
            Assert.That(seen.Count, Is.EqualTo(5));
        }

        [Test]
        public void FolderRegistration_DoesNotExpandChildrenIntoExplicitEntries()
        {
            CreateSettings();
            var group = Group("Folders");
            AssetDatabase.CreateFolder(Root, "Folder");
            File.WriteAllText(Root + "/Folder/Child.txt", "child");
            AssetDatabase.Refresh();
            _settings.GetType().GetMethod("CreateOrMoveEntry").Invoke(_settings,
                new object[] { AssetDatabase.AssetPathToGUID(Root + "/Folder"), group, false, true });
            var listed = Query("entry_list");
            Assert.That(listed["data"]["total"].Value<int>(), Is.EqualTo(1));
            Assert.That(listed["data"]["items"][0]["is_folder"].Value<bool>(), Is.True);
            var child = Query("entry_find", new JObject { ["path"] = Root + "/Folder/Child.txt" });
            Assert.That(child["data"]["found"].Value<bool>(), Is.False, child.ToString());
        }

        [Test]
        public void InvalidSelectorsAndCrossActionCursor_AreRefused()
        {
            CreateSettings();
            Group("One");
            Group("Two");
            var first = Query("group_list", new JObject { ["page_size"] = 1 });
            var wrongQuery = Query("label_list", new JObject { ["cursor"] = first["data"]["next_cursor"] });
            Assert.That(wrongQuery["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(Query("entry_list", new JObject { ["group"] = "missing" })["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            foreach (var selector in new[] {
                new JObject { ["path"] = "Assets/../outside.txt" },
                new JObject { ["guid"] = "invalid" },
                new JObject { ["path"] = Root + "/a.txt", ["guid"] = new string('a', 32) }
            })
                Assert.That(Query("entry_find", selector)["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
        }

        [Test]
        public void Profiles_ReturnRawVariablesAndUnknownProfileIsRefused()
        {
            CreateSettings();
            var profiles = Query("profile_list");
            Assert.That(profiles["success"].Value<bool>(), Is.True, profiles.ToString());
            Assert.That(profiles["data"]["items"].Count(), Is.GreaterThan(0));
            var profile = Query("profile_get");
            Assert.That(profile["success"].Value<bool>(), Is.True, profile.ToString());
            Assert.That(profile["data"]["items"].Any(row => row["value"].ToString().Contains("[")), Is.True);
            Assert.That(Query("profile_get", new JObject { ["profile_id"] = "missing" })["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_PROFILE_NOT_FOUND"));
        }

        private static void Succeeded(JObject result) => Assert.That(result["success"].Value<bool>(), Is.True, result.ToString());

        [Test]
        public void GroupCreateAndRemove_PersistSchemasAndPreserveSourceAssets()
        {
            CreateSettings();
            var created = Query("group_create", new JObject { ["name"] = "Runtime" });
            Succeeded(created);
            var group = ((IEnumerable)_settings.GetType().GetProperty("groups").GetValue(_settings)).Cast<object>().Single();
            Assert.That(((IEnumerable)group.GetType().GetProperty("Schemas").GetValue(group)).Cast<object>().Count(), Is.EqualTo(2));
            Assert.That(File.Exists(AssetDatabase.GetAssetPath((UnityEngine.Object)group)), Is.True);
            Assert.That(Query("group_create", new JObject { ["name"] = "Runtime" })["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            var entry = AddEntry("Preserved", group);
            Assert.That(Query("group_remove", new JObject { ["group"] = "Runtime" })["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Succeeded(Query("group_remove", new JObject { ["group"] = "Runtime", ["force"] = true }));
            Assert.That(File.Exists(Root + "/Preserved.txt"), Is.True);
            Assert.That(Query("group_list")["data"]["total"].Value<int>(), Is.Zero);
        }

        [Test]
        public void EntryLifecycle_RegistersMovesRenamesAndUnregistersOnly()
        {
            CreateSettings();
            Group("A");
            Group("B");
            File.WriteAllText(Root + "/Hero.txt", "hero");
            AssetDatabase.ImportAsset(Root + "/Hero.txt");
            var identity = new JObject { ["path"] = Root + "/Hero.txt", ["group"] = "A" };
            Succeeded(Query("entry_add", identity));
            Assert.That(Query("entry_add", identity)["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Succeeded(Query("entry_move", new JObject { ["path"] = Root + "/Hero.txt", ["group"] = "B" }));
            Assert.That(Query("entry_list", new JObject { ["group"] = "A" })["data"]["total"].Value<int>(), Is.Zero);
            Succeeded(Query("entry_set_address", new JObject { ["path"] = Root + "/Hero.txt", ["address"] = "actors/hero" }));
            Assert.That(Query("entry_find", new JObject { ["path"] = Root + "/Hero.txt" })["data"]["entry"]["address"].Value<string>(), Is.EqualTo("actors/hero"));
            var targetGroup = ((IEnumerable)_settings.GetType().GetProperty("groups").GetValue(_settings)).Cast<UnityEngine.Object>()
                .Single(group => group.name == "B");
            Assert.That(File.ReadAllText(AssetDatabase.GetAssetPath(targetGroup)), Does.Contain("actors/hero"));
            Succeeded(Query("entry_remove", new JObject { ["path"] = Root + "/Hero.txt" }));
            Assert.That(File.Exists(Root + "/Hero.txt"), Is.True);
            Assert.That(Query("entry_find", new JObject { ["path"] = Root + "/Hero.txt" })["data"]["found"].Value<bool>(), Is.False);
        }

        [Test]
        public void Labels_ReplaceClearAndRequireForceForUsedDefinitions()
        {
            CreateSettings();
            AddEntry("Hero", Group("Actors"));
            Succeeded(Query("label_add", new JObject { ["name"] = "combat" }));
            Succeeded(Query("label_add", new JObject { ["name"] = "combat" }));
            var identity = new JObject { ["path"] = Root + "/Hero.txt", ["labels"] = new JArray("combat") };
            Succeeded(Query("label_set", identity));
            Assert.That(Query("label_remove", new JObject { ["name"] = "combat" })["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            var invalid = Query("label_set", new JObject { ["path"] = Root + "/Hero.txt", ["labels"] = new JArray("unknown") });
            Assert.That(invalid["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(Query("entry_find", new JObject { ["path"] = Root + "/Hero.txt" })["data"]["entry"]["labels"].Values<string>(), Is.EquivalentTo(new[] { "combat" }));
            Succeeded(Query("label_set", new JObject { ["path"] = Root + "/Hero.txt", ["labels"] = new JArray() }));
            Assert.That(Query("entry_find", new JObject { ["path"] = Root + "/Hero.txt" })["data"]["entry"]["labels"].Count(), Is.Zero);
            Succeeded(Query("label_set", identity));
            Succeeded(Query("label_remove", new JObject { ["name"] = "combat", ["force"] = true }));
            Assert.That(Query("entry_find", new JObject { ["path"] = Root + "/Hero.txt" })["data"]["entry"]["labels"].Count(), Is.Zero);
            Assert.That(Query("label_list")["data"]["items"].Any(row => row["name"].Value<string>() == "combat"), Is.False);
        }

        [Test]
        public void Profiles_WriteRawValuesSelectProfileAndRejectMissingVariable()
        {
            CreateSettings();
            var profiles = _settings.GetType().GetProperty("profileSettings").GetValue(_settings);
            string originalId = Query("get_settings")["data"]["active_profile_id"].Value<string>();
            string id = (string)profiles.GetType().GetMethod("AddProfile").Invoke(profiles, new object[] { "Development", originalId });
            Succeeded(Query("profile_set", new JObject { ["profile_id"] = id, ["name"] = "Local.LoadPath", ["value"] = "[CustomToken]/raw" }));
            var result = Query("profile_get", new JObject { ["profile_id"] = id });
            Assert.That(result["data"]["items"].Single(row => row["name"].Value<string>() == "Local.LoadPath")["value"].Value<string>(), Is.EqualTo("[CustomToken]/raw"));
            Succeeded(Query("profile_set_active", new JObject { ["profile_id"] = id }));
            Assert.That(Query("get_settings")["data"]["active_profile_id"].Value<string>(), Is.EqualTo(id));
            Assert.That(Query("profile_set", new JObject { ["profile_id"] = id, ["name"] = "missing", ["value"] = "x" })["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
        }

        [Test]
        public void Settings_ValidateEntireUpdateBeforeWritingAndSupportUndo()
        {
            CreateSettings();
            int original = Query("get_settings")["data"]["values"]["max_concurrent_web_requests"].Value<int>();
            var invalid = Query("set_settings", new JObject { ["settings"] = new JObject { ["max_concurrent_web_requests"] = original + 1, ["unknown"] = true } });
            Assert.That(invalid["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(Query("get_settings")["data"]["values"]["max_concurrent_web_requests"].Value<int>(), Is.EqualTo(original));
            Undo.IncrementCurrentGroup();
            Succeeded(Query("set_settings", new JObject { ["settings"] = new JObject { ["max_concurrent_web_requests"] = original + 1 } }));
            Assert.That(Query("get_settings")["data"]["values"]["max_concurrent_web_requests"].Value<int>(), Is.EqualTo(original + 1));
            Undo.PerformUndo();
            Assert.That(Query("get_settings")["data"]["values"]["max_concurrent_web_requests"].Value<int>(), Is.EqualTo(original));
        }

        [Test]
        public void ReadOnlyEntriesAndInvalidAddresses_AreRefusedBeforeMutation()
        {
            CreateSettings();
            var entry = AddEntry("Hero", Group("Actors"));
            string original = (string)entry.GetType().GetProperty("address").GetValue(entry);
            var invalid = Query("entry_set_address", new JObject { ["path"] = Root + "/Hero.txt", ["address"] = "[bad]" });
            Assert.That(invalid["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(entry.GetType().GetProperty("address").GetValue(entry), Is.EqualTo(original));
            entry.GetType().GetProperty("ReadOnly").SetValue(entry, true);
            Assert.That(Query("entry_remove", new JObject { ["path"] = Root + "/Hero.txt" })["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(Query("entry_find", new JObject { ["path"] = Root + "/Hero.txt" })["data"]["found"].Value<bool>(), Is.True);
        }

        [TestCase("0")]
        [TestCase("1025")]
        [TestCase("2.7")]
        [TestCase("true")]
        public void Settings_InvalidIntegerValueDoesNotWrite(string value)
        {
            CreateSettings();
            string before = EditorJsonUtility.ToJson((UnityEngine.Object)_settings);
            var result = Query("set_settings", new JObject { ["settings"] = new JObject { ["max_concurrent_web_requests"] = JToken.Parse(value) } });
            Assert.That(result["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(EditorJsonUtility.ToJson((UnityEngine.Object)_settings), Is.EqualTo(before));
        }

        [Test]
        public void InvalidInitialization_DoesNotCreateDefaultSettings()
        {
            CreateSettings();
            EditorBuildSettings.RemoveConfigObject("com.unity.addressableassets");
            _defaultType.GetField("s_DefaultSettingsObject", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            string[] before = AssetDatabase.GetAllAssetPaths();
            var result = Query("set_settings", new JObject { ["initialize"] = true, ["settings"] = new JObject { ["unknown"] = true } });
            Assert.That(result["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            CollectionAssert.AreEquivalent(before, AssetDatabase.GetAllAssetPaths());
            Assert.That(Query("ping")["data"]["settings_exists"].Value<bool>(), Is.False);
        }

        [Test]
        public void BuildUpdate_RequiresRealPreviousStateBeforeScheduling()
        {
            CreateSettings();
            var missing = Query("build_update", new JObject { ["content_state_path"] = Root + "/missing.bin" });
            Assert.That(missing["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(missing["data"]["message"].Value<string>(), Does.Contain("content_state_path"));
            Assert.That(Query("build_status", new JObject { ["job_id"] = "unknown" })["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_BUILD_NOT_FOUND"));
        }

        [Test]
        public void CatalogPaths_ValidateVariablesBeforeChangingSettings()
        {
            CreateSettings();
            string before = EditorJsonUtility.ToJson((UnityEngine.Object)_settings);
            var invalid = Query("set_settings", new JObject { ["settings"] = new JObject
                { ["build_remote_catalog"] = true, ["remote_catalog_load_path"] = "missing" } });
            Assert.That(invalid["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(EditorJsonUtility.ToJson((UnityEngine.Object)_settings), Is.EqualTo(before));
            Succeeded(Query("set_settings", new JObject { ["settings"] = new JObject
                { ["remote_catalog_build_path"] = "Remote.BuildPath", ["remote_catalog_load_path"] = "Remote.LoadPath" } }));
            Assert.That(Query("get_settings")["data"]["values"]["remote_catalog_load_path"].Value<string>(), Is.Not.Empty);
        }

        [Test]
        public void InvalidLabelName_IsRejectedWithoutReportingCreation()
        {
            CreateSettings();
            string before = EditorJsonUtility.ToJson((UnityEngine.Object)_settings);
            var result = Query("label_add", new JObject { ["name"] = "[bad]" });
            Assert.That(result["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(EditorJsonUtility.ToJson((UnityEngine.Object)_settings), Is.EqualTo(before));
            Assert.That(Query("label_list")["data"]["items"].Any(row => row["name"].Value<string>() == "[bad]"), Is.False);
        }

        [Test]
        public void InvalidCatalogVariableInitialization_DoesNotPersistSettings()
        {
            CreateSettings();
            EditorBuildSettings.RemoveConfigObject("com.unity.addressableassets");
            _defaultType.GetField("s_DefaultSettingsObject", BindingFlags.NonPublic | BindingFlags.Static).SetValue(null, null);
            string[] before = AssetDatabase.GetAllAssetPaths();
            Assert.That(Query("ping")["data"]["settings_exists"].Value<bool>(), Is.False);
            var result = Query("set_settings", new JObject { ["initialize"] = true,
                ["settings"] = new JObject { ["remote_catalog_load_path"] = "missing-variable" } });
            Assert.That(result["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(Query("ping")["data"]["settings_exists"].Value<bool>(), Is.False);
            CollectionAssert.AreEquivalent(before, AssetDatabase.GetAllAssetPaths());
        }

        [Test]
        public void ForcedRemoval_StillProtectsReadOnlyEntries()
        {
            CreateSettings();
            var entry = AddEntry("Protected", Group("Actors"));
            Succeeded(Query("label_add", new JObject { ["name"] = "protected" }));
            Succeeded(Query("label_set", new JObject { ["path"] = Root + "/Protected.txt", ["labels"] = new JArray("protected") }));
            entry.GetType().GetProperty("ReadOnly").SetValue(entry, true);
            Assert.That(Query("label_remove", new JObject { ["name"] = "protected", ["force"] = true })["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(Query("group_remove", new JObject { ["group"] = "Actors", ["force"] = true })["code"].Value<string>(), Is.EqualTo("INVALID_ARGUMENT"));
            Assert.That(Query("entry_find", new JObject { ["path"] = Root + "/Protected.txt" })["data"]["found"].Value<bool>(), Is.True);
        }

        [Test]
        public void EntryAddress_UndoRestoresPreviousAddress()
        {
            CreateSettings();
            var entry = AddEntry("Hero", Group("Actors"));
            string original = (string)entry.GetType().GetProperty("address").GetValue(entry);
            Succeeded(Query("entry_set_address", new JObject { ["path"] = Root + "/Hero.txt", ["address"] = "actors/hero" }));
            Undo.PerformUndo();
            Assert.That(Query("entry_find", new JObject { ["path"] = Root + "/Hero.txt" })["data"]["entry"]["address"].Value<string>(), Is.EqualTo(original));
        }

        private static IEnumerator WaitForBuild(string id, Action<JObject> completed)
        {
            double deadline = EditorApplication.timeSinceStartup + 180;
            while (EditorApplication.timeSinceStartup < deadline)
            {
                var status = Query("build_status", new JObject { ["job_id"] = id });
                string state = status["data"]?["state"]?.Value<string>();
                if (state == "failed" || state == "succeeded")
                {
                    completed(status);
                    yield break;
                }
                yield return null;
            }
            Assert.Fail("Content operation did not complete. Inspect the recorded job before retrying.");
        }

        [UnityTest]
        public IEnumerator ContentBuild_UpdateRestrictionsAndClean_UseNativeBuilder()
        {
            CreateSettings();
            Succeeded(Query("group_create", new JObject { ["name"] = "Static", ["static_content"] = true }));
            Succeeded(Query("group_create", new JObject { ["name"] = "Dynamic", ["static_content"] = false }));
            var groups = (IEnumerable)_settings.GetType().GetProperty("groups").GetValue(_settings);
            var staticGroup = groups.Cast<object>().Single(group => (string)group.GetType().GetProperty("Name").GetValue(group) == "Static");
            _settings.GetType().GetProperty("DefaultGroup").SetValue(_settings, staticGroup);
            AddEntry("BuildHero", staticGroup);
            string profile = Query("get_settings")["data"]["active_profile_id"].Value<string>();
            Succeeded(Query("profile_set", new JObject { ["profile_id"] = profile, ["name"] = "Remote.LoadPath", ["value"] = "https://example.invalid/content/[BuildTarget]" }));
            Succeeded(Query("set_settings", new JObject { ["settings"] = new JObject
                { ["build_remote_catalog"] = true, ["remote_catalog_build_path"] = "Remote.BuildPath", ["remote_catalog_load_path"] = "Remote.LoadPath" } }));
            string stateCopy = Path.Combine(Path.GetTempPath(), Guid.NewGuid().ToString("N") + ".bin");
            try
            {
                var started = Query("build_content");
                Assert.That(started["data"]?["job_id"], Is.Not.Null, started.ToString());
                Assert.That(Query("group_create", new JObject { ["name"] = "DuringBuild" })["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_BUILD_BUSY"));
                JObject result = null;
                yield return WaitForBuild(started["data"]["job_id"].Value<string>(), value => result = value);
                Succeeded(result);
                Assert.That(File.Exists(result["data"]["output_path"].Value<string>()), Is.True);
                string contentStatePath = result["data"]["content_state_path"]?.Value<string>();
                Assert.That(contentStatePath, Is.Not.Null.And.Not.Empty, "A successful build must expose its content state on older and newer Addressables APIs.");
                Assert.That(File.Exists(contentStatePath), Is.True, contentStatePath);
                File.Copy(contentStatePath, stateCopy);
                File.WriteAllText(Root + "/BuildHero.txt", "modified content");
                AssetDatabase.ImportAsset(Root + "/BuildHero.txt", ImportAssetOptions.ForceUpdate);
                started = Query("build_update", new JObject { ["content_state_path"] = stateCopy });
                yield return WaitForBuild(started["data"]["job_id"].Value<string>(), value => result = value);
                Assert.That(result["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_BUILD_FAILED"));
                Assert.That(result["data"]["restricted_entries"].Count(), Is.GreaterThan(0));
                Succeeded(Query("entry_move", new JObject { ["path"] = Root + "/BuildHero.txt", ["group"] = "Dynamic" }));
                started = Query("build_update", new JObject { ["content_state_path"] = stateCopy });
                yield return WaitForBuild(started["data"]["job_id"].Value<string>(), value => result = value);
                Succeeded(result);
                started = Query("build_clean");
                yield return WaitForBuild(started["data"]["job_id"].Value<string>(), value => result = value);
                Succeeded(result);
            }
            finally
            {
                if (File.Exists(stateCopy)) File.Delete(stateCopy);
            }
        }

        [Test]
        public void MissingGroupIdentifiers_QueryDoesNotGenerateThem()
        {
            CreateSettings();
            var group = (UnityEngine.Object)Group("Actors");
            using (var serialized = new SerializedObject(group))
            {
                serialized.FindProperty("m_GUID").stringValue = "";
                serialized.FindProperty("m_GroupName").stringValue = "";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            string before = EditorJsonUtility.ToJson(group);
            var result = Query("group_list");
            Assert.That(result["success"].Value<bool>(), Is.True, result.ToString());
            Assert.That(result["data"]["items"][0]["guid"].Value<string>(), Is.Empty);
            Assert.That(result["data"]["items"][0]["name"].Value<string>(), Is.Empty);
            Assert.That(EditorJsonUtility.ToJson(group), Is.EqualTo(before));
        }

        [Test]
        public void MissingActiveProfile_QueriesDoNotRepairConfiguration()
        {
            CreateSettings();
            var asset = (UnityEngine.Object)_settings;
            using (var serialized = new SerializedObject(asset))
            {
                serialized.FindProperty("m_ActiveProfileId").stringValue = "";
                serialized.ApplyModifiedPropertiesWithoutUndo();
            }
            string before = EditorJsonUtility.ToJson(asset);
            var settings = Query("get_settings");
            Assert.That(settings["success"].Value<bool>(), Is.True, settings.ToString());
            Assert.That(settings["data"]["active_profile_id"].Value<string>(), Is.Empty);
            var profile = Query("profile_get");
            Assert.That(profile["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_PROFILE_NOT_FOUND"));
            Assert.That(EditorJsonUtility.ToJson(asset), Is.EqualTo(before));
        }

        [Test]
        public void LegacySettings_QueryDoesNotMigrateConfiguration()
        {
            CreateSettings();
            EditorBuildSettings.RemoveConfigObject("com.unity.addressableassets");
            EditorBuildSettings.AddConfigObject("AddressableAssetSettings", (UnityEngine.Object)_settings, true);
            var result = Query("get_settings");
            Assert.That(result["code"].Value<string>(), Is.EqualTo("ADDRESSABLES_MIGRATION_REQUIRED"));
            Assert.That(EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>("AddressableAssetSettings", out _), Is.True);
            Assert.That(EditorBuildSettings.TryGetConfigObject<UnityEngine.Object>("com.unity.addressableassets", out _), Is.False);
        }
    }
}
