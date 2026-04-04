using System;
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;
using MCPForUnity.Editor.Tools.Graphics;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageGraphicsTests
    {
        private const string TempRoot = "Assets/Temp/ManageGraphicsTests";
        private bool _hasVolumeSystem;
        private bool _hasURP;
        private bool _hasHDRP;

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TempRoot);

            var pingResult = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "ping" }));
            if (pingResult.Value<bool>("success"))
            {
                var data = pingResult["data"];
                _hasVolumeSystem = data?.Value<bool>("hasVolumeSystem") ?? false;
                _hasURP = data?.Value<bool>("hasURP") ?? false;
                _hasHDRP = data?.Value<bool>("hasHDRP") ?? false;
            }
        }

        [TearDown]
        public void TearDown()
        {
#if UNITY_2022_2_OR_NEWER
            foreach (var go in UnityEngine.Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
#else
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
#endif
            {
                if (go.name.StartsWith("GfxTest_"))
                    UnityEngine.Object.DestroyImmediate(go);
            }

            if (AssetDatabase.IsValidFolder(TempRoot))
                AssetDatabase.DeleteAsset(TempRoot);
            CleanupEmptyParentFolders(TempRoot);

            // Reset scene debug mode
            ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "stats_set_scene_debug",
                ["mode"] = "Textured"
            });
        }

        // =====================================================================
        // Dispatch / Error Handling
        // =====================================================================

        [Test]
        public void HandleCommand_NullParams_ReturnsError()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(null));
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void HandleCommand_MissingAction_ReturnsError()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject()));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("action"));
        }

        [Test]
        public void HandleCommand_UnknownAction_ReturnsError()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "bogus_action" }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("Unknown action"));
        }

        [Test]
        public void Ping_ReturnsPipelineInfo()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "ping" }));
            Assert.IsTrue(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Pipeline"));
            var data = result["data"];
            Assert.IsNotNull(data);
            Assert.IsNotNull(data["pipeline"]);
            Assert.IsNotNull(data["pipelineName"]);
        }

        // =====================================================================
        // Volume Actions
        // =====================================================================

        private void AssumeVolumeSystem()
        {
            Assume.That(_hasVolumeSystem, "Volume system not available — skipping.");
        }

        [Test]
        public void VolumeCreate_Global_CreatesVolume()
        {
            AssumeVolumeSystem();
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_create",
                ["name"] = "GfxTest_Volume",
                ["is_global"] = true,
                ["priority"] = 10
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsTrue(result["data"]["isGlobal"].Value<bool>());
            Assert.AreEqual(10, result["data"]["priority"].Value<int>());
        }

        [Test]
        public void VolumeCreate_WithEffects_AddsEffects()
        {
            AssumeVolumeSystem();
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_create",
                ["name"] = "GfxTest_VolumeEffects",
                ["effects"] = new JArray
                {
                    new JObject { ["type"] = "Bloom", ["intensity"] = 2 },
                    new JObject { ["type"] = "Vignette", ["intensity"] = 0.5 }
                }
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var effects = result["data"]["effects"] as JArray;
            Assert.IsNotNull(effects);
            Assert.AreEqual(2, effects.Count);
        }

        [Test]
        public void VolumeCreate_Local_CreatesNonGlobal()
        {
            AssumeVolumeSystem();
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_create",
                ["name"] = "GfxTest_LocalVol",
                ["is_global"] = false
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsFalse(result["data"]["isGlobal"].Value<bool>());
        }

        [Test]
        public void VolumeAddEffect_AddsEffect()
        {
            AssumeVolumeSystem();
            CreateTestVolume("GfxTest_AddFx");

            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_add_effect",
                ["target"] = "GfxTest_AddFx",
                ["effect"] = "Bloom",
                ["parameters"] = new JObject { ["intensity"] = 3 }
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual("Bloom", result["data"]["effect"].ToString());
        }

        [Test]
        public void VolumeAddEffect_Duplicate_ReturnsError()
        {
            AssumeVolumeSystem();
            CreateTestVolume("GfxTest_DupFx");
            ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_add_effect",
                ["target"] = "GfxTest_DupFx",
                ["effect"] = "Bloom"
            });

            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_add_effect",
                ["target"] = "GfxTest_DupFx",
                ["effect"] = "Bloom"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("already exists"));
        }

        [Test]
        public void VolumeAddEffect_InvalidEffect_ReturnsError()
        {
            AssumeVolumeSystem();
            CreateTestVolume("GfxTest_BadFx");

            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_add_effect",
                ["target"] = "GfxTest_BadFx",
                ["effect"] = "FakeEffect"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
        }

        [Test]
        public void VolumeSetEffect_SetsParameters()
        {
            AssumeVolumeSystem();
            CreateTestVolume("GfxTest_SetFx");
            ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_add_effect",
                ["target"] = "GfxTest_SetFx",
                ["effect"] = "Bloom"
            });

            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_set_effect",
                ["target"] = "GfxTest_SetFx",
                ["effect"] = "Bloom",
                ["parameters"] = new JObject { ["intensity"] = 5, ["scatter"] = 0.8 }
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var setParams = result["data"]["set"] as JArray;
            Assert.IsNotNull(setParams);
            Assert.That(setParams.Select(t => t.ToString()), Contains.Item("intensity"));
            Assert.That(setParams.Select(t => t.ToString()), Contains.Item("scatter"));
        }

        [Test]
        public void VolumeSetEffect_InvalidParam_ReportsFailed()
        {
            AssumeVolumeSystem();
            CreateTestVolume("GfxTest_BadParam");
            ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_add_effect",
                ["target"] = "GfxTest_BadParam",
                ["effect"] = "Bloom"
            });

            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_set_effect",
                ["target"] = "GfxTest_BadParam",
                ["effect"] = "Bloom",
                ["parameters"] = new JObject { ["nonExistent"] = 42 }
            }));
            Assert.IsTrue(result.Value<bool>("success"));
            var failed = result["data"]["failed"] as JArray;
            Assert.IsNotNull(failed);
            Assert.That(failed.Select(t => t.ToString()), Contains.Item("nonExistent"));
        }

        [Test]
        public void VolumeRemoveEffect_RemovesEffect()
        {
            AssumeVolumeSystem();
            CreateTestVolume("GfxTest_RmFx");
            ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_add_effect",
                ["target"] = "GfxTest_RmFx",
                ["effect"] = "Vignette"
            });

            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_remove_effect",
                ["target"] = "GfxTest_RmFx",
                ["effect"] = "Vignette"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            // Verify it's gone
            var info = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_get_info",
                ["target"] = "GfxTest_RmFx"
            }));
            var effects = info["data"]["effects"] as JArray;
            Assert.IsNotNull(effects);
            Assert.AreEqual(0, effects.Count);
        }

        [Test]
        public void VolumeRemoveEffect_NonExistent_ReturnsError()
        {
            AssumeVolumeSystem();
            CreateTestVolume("GfxTest_RmMissing");

            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_remove_effect",
                ["target"] = "GfxTest_RmMissing",
                ["effect"] = "DepthOfField"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
        }

        [Test]
        public void VolumeGetInfo_ReturnsEffectList()
        {
            AssumeVolumeSystem();
            CreateTestVolume("GfxTest_Info");
            ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_add_effect",
                ["target"] = "GfxTest_Info",
                ["effect"] = "Bloom"
            });

            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_get_info",
                ["target"] = "GfxTest_Info"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"];
            Assert.AreEqual("GfxTest_Info", data["name"].ToString());
            var effects = data["effects"] as JArray;
            Assert.IsNotNull(effects);
            Assert.AreEqual(1, effects.Count);
            Assert.AreEqual("Bloom", effects[0]["type"].ToString());
        }

        [Test]
        public void VolumeGetInfo_NonExistentTarget_ReturnsError()
        {
            AssumeVolumeSystem();
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_get_info",
                ["target"] = "NonExistentVolume"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void VolumeSetProperties_UpdatesWeightAndPriority()
        {
            AssumeVolumeSystem();
            CreateTestVolume("GfxTest_Props");

            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_set_properties",
                ["target"] = "GfxTest_Props",
                ["properties"] = new JObject { ["weight"] = 0.5, ["priority"] = 20 }
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var changed = result["data"]["changed"] as JArray;
            Assert.IsNotNull(changed);
            Assert.That(changed.Select(t => t.ToString()), Contains.Item("weight"));
            Assert.That(changed.Select(t => t.ToString()), Contains.Item("priority"));

            // Verify via get_info
            var info = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_get_info",
                ["target"] = "GfxTest_Props"
            }));
            Assert.AreEqual(0.5f, info["data"]["weight"].Value<float>(), 0.01f);
            Assert.AreEqual(20f, info["data"]["priority"].Value<float>(), 0.01f);
        }

        [Test]
        public void VolumeListEffects_ReturnsAvailableTypes()
        {
            AssumeVolumeSystem();
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "volume_list_effects" }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var effects = result["data"]["effects"] as JArray;
            Assert.IsNotNull(effects);
            Assert.Greater(effects.Count, 0);
            Assert.IsNotNull(effects[0]["name"]);
        }

        [Test]
        public void VolumeCreateProfile_CreatesAsset()
        {
            AssumeVolumeSystem();
            string path = $"{TempRoot}/TestProfile";
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_create_profile",
                ["path"] = path
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            string fullPath = result["data"]["path"].ToString();
            Assert.IsTrue(fullPath.EndsWith(".asset"));
            Assert.IsNotNull(AssetDatabase.LoadAssetAtPath<UnityEngine.Object>(fullPath));
        }

        // =====================================================================
        // Bake Actions
        // =====================================================================

        [Test]
        public void BakeGetSettings_ReturnsLightmapperInfo()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "bake_get_settings" }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var data = result["data"];
            Assert.IsNotNull(data["lightmapper"]);
            Assert.IsNotNull(data["lightmapResolution"]);
        }

        [Test]
        public void BakeSetSettings_ChangesAndRestores()
        {
            // Read original
            var original = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "bake_get_settings" }));
            int origResolution = original["data"]["lightmapResolution"].Value<int>();

            // Change
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "bake_set_settings",
                ["settings"] = new JObject { ["lightmapResolution"] = 20 }
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var changed = result["data"]["changed"] as JArray;
            Assert.That(changed.Select(t => t.ToString()), Contains.Item("lightmapResolution"));

            // Verify
            var verify = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "bake_get_settings" }));
            Assert.AreEqual(20, verify["data"]["lightmapResolution"].Value<int>());

            // Restore
            ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "bake_set_settings",
                ["settings"] = new JObject { ["lightmapResolution"] = origResolution }
            });
        }

        [Test]
        public void BakeStatus_ReportsNotRunning()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "bake_status" }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]["isRunning"]);
        }

        [Test]
        public void BakeClear_Succeeds()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "bake_clear" }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void BakeCreateReflectionProbe_CreatesProbe()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "bake_create_reflection_probe",
                ["name"] = "GfxTest_ReflProbe",
                ["position"] = new JArray(0, 1, 0),
                ["size"] = new JArray(10, 10, 10),
                ["resolution"] = 128
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var go = GameObject.Find("GfxTest_ReflProbe");
            Assert.IsNotNull(go);
            Assert.IsNotNull(go.GetComponent<ReflectionProbe>());
        }

        [Test]
        public void BakeCreateLightProbeGroup_CreatesGrid()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "bake_create_light_probe_group",
                ["name"] = "GfxTest_LPGroup",
                ["position"] = new JArray(0, 0, 0),
                ["grid_size"] = new JArray(2, 2, 2),
                ["spacing"] = 2
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(8, result["data"]["probeCount"].Value<int>());
            var go = GameObject.Find("GfxTest_LPGroup");
            Assert.IsNotNull(go);
            Assert.IsNotNull(go.GetComponent<LightProbeGroup>());
        }

        [Test]
        public void BakeSetProbePositions_SetsPositions()
        {
            ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "bake_create_light_probe_group",
                ["name"] = "GfxTest_LPPos",
                ["grid_size"] = new JArray(1, 1, 1)
            });

            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "bake_set_probe_positions",
                ["target"] = "GfxTest_LPPos",
                ["positions"] = new JArray
                {
                    new JArray(0, 0, 0),
                    new JArray(1, 0, 0),
                    new JArray(0, 1, 0)
                }
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.AreEqual(3, result["data"]["probeCount"].Value<int>());
        }

        [Test]
        public void BakeSetProbePositions_WrongComponent_ReturnsError()
        {
            var go = new GameObject("GfxTest_NoProbe");
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "bake_set_probe_positions",
                ["target"] = "GfxTest_NoProbe",
                ["positions"] = new JArray { new JArray(0, 0, 0) }
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("LightProbeGroup"));
        }

        // =====================================================================
        // Stats Actions
        // =====================================================================

        [Test]
        public void StatsGet_ReturnsCounters()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "stats_get" }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]["draw_calls"]);
            Assert.IsNotNull(result["data"]["batches"]);
            Assert.IsNotNull(result["data"]["triangles"]);
        }

        [Test]
        public void StatsListCounters_ReturnsList()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "stats_list_counters" }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var counters = result["data"]["counters"] as JArray;
            Assert.IsNotNull(counters);
            Assert.Greater(counters.Count, 0);
        }

        [Test]
        public void StatsGetMemory_ReturnsMemoryInfo()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "stats_get_memory" }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]["totalAllocatedMB"]);
            Assert.IsNotNull(result["data"]["graphicsDriverMB"]);
        }

        [Test]
        public void StatsSetSceneDebug_ValidMode_Succeeds()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "stats_set_scene_debug",
                ["mode"] = "Wireframe"
            }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
        }

        [Test]
        public void StatsSetSceneDebug_InvalidMode_ReturnsError()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "stats_set_scene_debug",
                ["mode"] = "InvalidMode"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("Valid:"));
        }

        // =====================================================================
        // Pipeline Actions
        // =====================================================================

        [Test]
        public void PipelineGetInfo_ReturnsPipelineName()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "pipeline_get_info" }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]["pipelineName"]);
            Assert.IsNotNull(result["data"]["qualityLevelName"]);
        }

        [Test]
        public void PipelineGetSettings_ReturnsSettings()
        {
            Assume.That(_hasURP || _hasHDRP, "Built-in pipeline has no settings asset — skipping.");
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "pipeline_get_settings" }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            var settings = result["data"]["settings"];
            Assert.IsNotNull(settings);
            Assert.IsNotNull(settings["renderScale"]);
        }

        [Test]
        public void PipelineSetQuality_InvalidLevel_ReturnsError()
        {
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "pipeline_set_quality",
                ["level"] = "NonExistentLevel"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["error"].ToString(), Does.Contain("Available:"));
        }

        // =====================================================================
        // Renderer Feature Actions (URP only)
        // =====================================================================

        private void AssumeURP()
        {
            Assume.That(_hasURP, "URP not available — skipping.");
        }

        [Test]
        public void FeatureList_ReturnsFeatures()
        {
            AssumeURP();
            var result = ToJObject(ManageGraphics.HandleCommand(
                new JObject { ["action"] = "feature_list" }));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());
            Assert.IsNotNull(result["data"]["features"]);
            Assert.IsNotNull(result["data"]["rendererDataName"]);
        }

        [Test]
        public void FeatureAdd_InvalidType_ReturnsError()
        {
            AssumeURP();
            var result = ToJObject(ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "feature_add",
                ["feature_type"] = "NonExistentFeature"
            }));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
            Assert.That(result["message"].ToString(), Does.Contain("Available:"));
        }

        // =====================================================================
        // Helpers
        // =====================================================================

        private void CreateTestVolume(string name)
        {
            ManageGraphics.HandleCommand(new JObject
            {
                ["action"] = "volume_create",
                ["name"] = name
            });
        }
    }
}
