using System;
using System.IO;
using System.Linq;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using MCPForUnity.Editor.Tools.Animation;
using static MCPForUnityTests.Editor.TestUtilities;

namespace MCPForUnityTests.Editor.Tools
{
    public class ManageAnimationTests
    {
        private const string TempRoot = "Assets/Temp/ManageAnimationTests";

        [SetUp]
        public void SetUp()
        {
            EnsureFolder(TempRoot);
        }

        [TearDown]
        public void TearDown()
        {
            // Clean up scene objects
            foreach (var go in UnityEngine.Object.FindObjectsOfType<GameObject>())
            {
                if (go.name.StartsWith("AnimTest_"))
                {
                    UnityEngine.Object.DestroyImmediate(go);
                }
            }

            if (AssetDatabase.IsValidFolder(TempRoot))
            {
                AssetDatabase.DeleteAsset(TempRoot);
            }
            CleanupEmptyParentFolders(TempRoot);
        }

        // =============================================================================
        // Dispatch / Error Handling
        // =============================================================================

        [Test]
        public void HandleCommand_MissingAction_ReturnsError()
        {
            var paramsObj = new JObject();
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Action is required"));
        }

        [Test]
        public void HandleCommand_UnknownAction_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "bogus_action" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Unknown action"));
        }

        [Test]
        public void HandleCommand_UnknownAnimatorAction_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "animator_nonexistent" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Unknown animator action"));
        }

        [Test]
        public void HandleCommand_UnknownClipAction_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "clip_nonexistent" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Unknown clip action"));
        }

        // =============================================================================
        // Animator: Get Info
        // =============================================================================

        [Test]
        public void AnimatorGetInfo_NoTarget_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "animator_get_info" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
        }

        [Test]
        public void AnimatorGetInfo_NoAnimator_ReturnsError()
        {
            var go = new GameObject("AnimTest_NoAnimator");
            try
            {
                var paramsObj = new JObject
                {
                    ["action"] = "animator_get_info",
                    ["target"] = "AnimTest_NoAnimator"
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsFalse(result.Value<bool>("success"));
                Assert.That(result["message"].ToString(), Does.Contain("No Animator"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AnimatorGetInfo_WithAnimator_ReturnsData()
        {
            var go = new GameObject("AnimTest_WithAnimator");
            go.AddComponent<Animator>();
            try
            {
                var paramsObj = new JObject
                {
                    ["action"] = "animator_get_info",
                    ["target"] = "AnimTest_WithAnimator"
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), result.ToString());
                var data = result["data"] as JObject;
                Assert.IsNotNull(data);
                Assert.AreEqual("AnimTest_WithAnimator", data["gameObject"].ToString());
                Assert.IsNotNull(data["enabled"]);
                Assert.IsNotNull(data["speed"]);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =============================================================================
        // Animator: Set Speed / Set Enabled
        // =============================================================================

        [Test]
        public void AnimatorSetSpeed_ChangesSpeed()
        {
            var go = new GameObject("AnimTest_Speed");
            var animator = go.AddComponent<Animator>();
            try
            {
                var paramsObj = new JObject
                {
                    ["action"] = "animator_set_speed",
                    ["target"] = "AnimTest_Speed",
                    ["speed"] = 2.5f
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), result.ToString());
                Assert.AreEqual(2.5f, animator.speed, 0.001f);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void AnimatorSetEnabled_DisablesAnimator()
        {
            var go = new GameObject("AnimTest_Enabled");
            var animator = go.AddComponent<Animator>();
            Assert.IsTrue(animator.enabled);
            try
            {
                var paramsObj = new JObject
                {
                    ["action"] = "animator_set_enabled",
                    ["target"] = "AnimTest_Enabled",
                    ["enabled"] = false
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), result.ToString());
                Assert.IsFalse(animator.enabled);
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =============================================================================
        // Clip: Create
        // =============================================================================

        [Test]
        public void ClipCreate_CreatesAsset()
        {
            string clipPath = $"{TempRoot}/TestClip_{Guid.NewGuid():N}.anim";

            var paramsObj = new JObject
            {
                ["action"] = "clip_create",
                ["clipPath"] = clipPath,
                ["length"] = 2.0f,
                ["loop"] = true
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            Assert.IsNotNull(clip, "Clip asset should exist");

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            Assert.IsTrue(settings.loopTime, "Clip should be looping");
            Assert.AreEqual(2.0f, settings.stopTime, 0.01f);
        }

        [Test]
        public void ClipCreate_DuplicatePath_ReturnsError()
        {
            string clipPath = $"{TempRoot}/DuplicateClip.anim";

            // Create first
            var clip = new AnimationClip { name = "DuplicateClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            // Try to create again
            var paramsObj = new JObject
            {
                ["action"] = "clip_create",
                ["clipPath"] = clipPath,
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("already exists"));
        }

        [Test]
        public void ClipCreate_MissingPath_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "clip_create" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("clipPath"));
        }

        // =============================================================================
        // Clip: Get Info
        // =============================================================================

        [Test]
        public void ClipGetInfo_ReturnsClipData()
        {
            string clipName = $"InfoClip_{Guid.NewGuid():N}";
            string clipPath = $"{TempRoot}/{clipName}.anim";
            var clip = new AnimationClip { name = clipName, frameRate = 30f };
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            settings.loopTime = true;
            settings.stopTime = 1.5f;
            AnimationUtility.SetAnimationClipSettings(clip, settings);
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_get_info",
                ["clipPath"] = clipPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(clipName, data["name"].ToString());
            Assert.AreEqual(30f, data.Value<float>("frameRate"), 0.01f);
            Assert.IsTrue(data.Value<bool>("isLooping"));
        }

        [Test]
        public void ClipGetInfo_NotFound_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["action"] = "clip_get_info",
                ["clipPath"] = "Assets/Nonexistent.anim"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
        }

        // =============================================================================
        // Clip: Add / Set Curve
        // =============================================================================

        [Test]
        public void ClipAddCurve_AddsKeyframes()
        {
            string clipPath = $"{TempRoot}/CurveClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "CurveClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_add_curve",
                ["clipPath"] = clipPath,
                ["propertyPath"] = "localPosition.y",
                ["type"] = "Transform",
                ["keys"] = new JArray(
                    new JArray(0f, 0f),
                    new JArray(0.5f, 2f),
                    new JArray(1f, 0f)
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            // Verify curve was added
            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length);
            Assert.AreEqual("localPosition.y", bindings[0].propertyName);

            var curve = AnimationUtility.GetEditorCurve(clip, bindings[0]);
            Assert.AreEqual(3, curve.length);
        }

        [Test]
        public void ClipSetCurve_ReplacesKeyframes()
        {
            string clipPath = $"{TempRoot}/SetCurveClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "SetCurveClip" };

            // Add initial curve
            var initialCurve = new AnimationCurve(new Keyframe(0, 0), new Keyframe(1, 1));
            var binding = EditorCurveBinding.FloatCurve("", typeof(Transform), "localPosition.x");
            AnimationUtility.SetEditorCurve(clip, binding, initialCurve);

            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            // Replace with new keyframes
            var paramsObj = new JObject
            {
                ["action"] = "clip_set_curve",
                ["clipPath"] = clipPath,
                ["propertyPath"] = "localPosition.x",
                ["type"] = "Transform",
                ["keys"] = new JArray(
                    new JArray(0f, 5f),
                    new JArray(2f, 10f)
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            Assert.AreEqual(2, curve.length);
            Assert.AreEqual(5f, curve.keys[0].value, 0.01f);
            Assert.AreEqual(10f, curve.keys[1].value, 0.01f);
        }

        [Test]
        public void ClipAddCurve_WithObjectFormat_ParsesKeyframes()
        {
            string clipPath = $"{TempRoot}/ObjFormatClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "ObjFormatClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_add_curve",
                ["clipPath"] = clipPath,
                ["propertyPath"] = "localPosition.z",
                ["type"] = "Transform",
                ["keys"] = new JArray(
                    new JObject { ["time"] = 0f, ["value"] = 0f, ["inTangent"] = 0f, ["outTangent"] = 1f },
                    new JObject { ["time"] = 1f, ["value"] = 5f }
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            Assert.AreEqual(1, bindings.Length);
            var curve = AnimationUtility.GetEditorCurve(clip, bindings[0]);
            Assert.AreEqual(2, curve.length);
            Assert.AreEqual(1f, curve.keys[0].outTangent, 0.01f);
        }

        [Test]
        public void ClipAddCurve_MissingKeys_ReturnsError()
        {
            string clipPath = $"{TempRoot}/NoKeysClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "NoKeysClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_add_curve",
                ["clipPath"] = clipPath,
                ["propertyPath"] = "localPosition.y",
                ["type"] = "Transform",
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("keys"));
        }

        // =============================================================================
        // Clip: Assign
        // =============================================================================

        [Test]
        public void ClipAssign_AddsAnimationComponent()
        {
            string clipPath = $"{TempRoot}/AssignClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "AssignClip" };
            clip.legacy = true;
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var go = new GameObject("AnimTest_Assign");
            try
            {
                Assert.IsNull(go.GetComponent<UnityEngine.Animation>());
                Assert.IsNull(go.GetComponent<Animator>());

                var paramsObj = new JObject
                {
                    ["action"] = "clip_assign",
                    ["target"] = "AnimTest_Assign",
                    ["clipPath"] = clipPath
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), result.ToString());

                var anim = go.GetComponent<UnityEngine.Animation>();
                Assert.IsNotNull(anim, "Should have added Animation component");
                Assert.IsNotNull(anim.clip, "Should have assigned clip");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void ClipAssign_MissingClip_ReturnsError()
        {
            var go = new GameObject("AnimTest_AssignMissing");
            try
            {
                var paramsObj = new JObject
                {
                    ["action"] = "clip_assign",
                    ["target"] = "AnimTest_AssignMissing",
                    ["clipPath"] = "Assets/Nonexistent.anim"
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsFalse(result.Value<bool>("success"));
                Assert.That(result["message"].ToString(), Does.Contain("not found"));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =============================================================================
        // Parameter Normalization
        // =============================================================================

        [Test]
        public void HandleCommand_SnakeCaseParams_Normalized()
        {
            // Test that snake_case parameters like clip_path get normalized to clipPath
            string clipPath = $"{TempRoot}/SnakeCase_{Guid.NewGuid():N}.anim";
            var paramsObj = new JObject
            {
                ["action"] = "clip_create",
                ["clip_path"] = clipPath,
                ["length"] = 1.0f
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            Assert.IsNotNull(clip);
        }

        [Test]
        public void HandleCommand_PropertiesDict_Flattened()
        {
            // Test that properties dict is flattened into top-level params
            string clipPath = $"{TempRoot}/PropsFlat_{Guid.NewGuid():N}.anim";
            var paramsObj = new JObject
            {
                ["action"] = "clip_create",
                ["properties"] = new JObject
                {
                    ["clipPath"] = clipPath,
                    ["length"] = 1.5f,
                    ["loop"] = true
                }
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            Assert.IsNotNull(clip);
            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            Assert.IsTrue(settings.loopTime);
        }

        // =============================================================================
        // Controller: Dispatch
        // =============================================================================

        [Test]
        public void HandleCommand_UnknownControllerAction_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "controller_nonexistent" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Unknown controller action"));
        }

        // =============================================================================
        // Controller: Create
        // =============================================================================

        [Test]
        public void ControllerCreate_CreatesAsset()
        {
            string controllerPath = $"{TempRoot}/TestController_{Guid.NewGuid():N}.controller";

            var paramsObj = new JObject
            {
                ["action"] = "controller_create",
                ["controllerPath"] = controllerPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.IsNotNull(controller, "Controller asset should exist");
        }

        [Test]
        public void ControllerCreate_DuplicatePath_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/DuplicateController.controller";

            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_create",
                ["controllerPath"] = controllerPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("already exists"));
        }

        [Test]
        public void ControllerCreate_MissingPath_ReturnsError()
        {
            var paramsObj = new JObject { ["action"] = "controller_create" };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("controllerPath"));
        }

        // =============================================================================
        // Controller: Add State
        // =============================================================================

        [Test]
        public void ControllerAddState_AddsState()
        {
            string controllerPath = $"{TempRoot}/StateController_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Walk"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var states = controller.layers[0].stateMachine.states;
            Assert.IsTrue(states.Any(s => s.state.name == "Walk"), "State 'Walk' should exist");
        }

        [Test]
        public void ControllerAddState_DuplicateName_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/DupStateController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.layers[0].stateMachine.AddState("Idle");
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Idle"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("already exists"));
        }

        [Test]
        public void ControllerAddState_WithClip_AssignsMotion()
        {
            string controllerPath = $"{TempRoot}/MotionController_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);

            string clipPath = $"{TempRoot}/MotionClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "MotionClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_state",
                ["controllerPath"] = controllerPath,
                ["stateName"] = "Run",
                ["clipPath"] = clipPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var state = controller.layers[0].stateMachine.states.First(s => s.state.name == "Run").state;
            Assert.IsNotNull(state.motion, "State should have motion assigned");
        }

        // =============================================================================
        // Controller: Add Transition
        // =============================================================================

        [Test]
        public void ControllerAddTransition_AddsTransition()
        {
            string controllerPath = $"{TempRoot}/TransController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            var sm = controller.layers[0].stateMachine;
            sm.AddState("Idle");
            sm.AddState("Walk");
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["hasExitTime"] = false,
                ["duration"] = 0.1f
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idleState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(1, idleState.transitions.Length);
            Assert.AreEqual("Walk", idleState.transitions[0].destinationState.name);
            Assert.IsFalse(idleState.transitions[0].hasExitTime);
        }

        [Test]
        public void ControllerAddTransition_WithConditions_AddsConditions()
        {
            string controllerPath = $"{TempRoot}/CondController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;
            sm.AddState("Idle");
            sm.AddState("Walk");
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Walk",
                ["conditions"] = new JArray(
                    new JObject
                    {
                        ["parameter"] = "Speed",
                        ["mode"] = "greater",
                        ["threshold"] = 0.1f
                    }
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            var idleState = controller.layers[0].stateMachine.states.First(s => s.state.name == "Idle").state;
            Assert.AreEqual(1, idleState.transitions[0].conditions.Length);
            Assert.AreEqual("Speed", idleState.transitions[0].conditions[0].parameter);
        }

        [Test]
        public void ControllerAddTransition_MissingState_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/MissStateController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.layers[0].stateMachine.AddState("Idle");
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_transition",
                ["controllerPath"] = controllerPath,
                ["fromState"] = "Idle",
                ["toState"] = "Nonexistent"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("not found"));
        }

        // =============================================================================
        // Controller: Add Parameter
        // =============================================================================

        [Test]
        public void ControllerAddParameter_AddsParameter()
        {
            string controllerPath = $"{TempRoot}/ParamController_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_parameter",
                ["controllerPath"] = controllerPath,
                ["parameterName"] = "Speed",
                ["parameterType"] = "float",
                ["defaultValue"] = 1.5f
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.IsTrue(controller.parameters.Any(p => p.name == "Speed"), "Parameter 'Speed' should exist");
            var param = controller.parameters.First(p => p.name == "Speed");
            Assert.AreEqual(AnimatorControllerParameterType.Float, param.type);
            Assert.AreEqual(1.5f, param.defaultFloat, 0.01f);
        }

        [Test]
        public void ControllerAddParameter_DuplicateName_ReturnsError()
        {
            string controllerPath = $"{TempRoot}/DupParamController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_add_parameter",
                ["controllerPath"] = controllerPath,
                ["parameterName"] = "Speed",
                ["parameterType"] = "float"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("already exists"));
        }

        [Test]
        public void ControllerAddParameter_AllTypes()
        {
            string controllerPath = $"{TempRoot}/AllTypesController_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            string[] types = { "float", "int", "bool", "trigger" };
            foreach (var t in types)
            {
                var paramsObj = new JObject
                {
                    ["action"] = "controller_add_parameter",
                    ["controllerPath"] = controllerPath,
                    ["parameterName"] = $"Param_{t}",
                    ["parameterType"] = t
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), $"Failed for type {t}: {result}");
            }

            var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(controllerPath);
            Assert.AreEqual(4, controller.parameters.Length);
        }

        // =============================================================================
        // Controller: Get Info
        // =============================================================================

        [Test]
        public void ControllerGetInfo_ReturnsData()
        {
            string controllerPath = $"{TempRoot}/InfoController_{Guid.NewGuid():N}.controller";
            var controller = AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
            var sm = controller.layers[0].stateMachine;
            sm.AddState("Idle");
            sm.AddState("Walk");
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "controller_get_info",
                ["controllerPath"] = controllerPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(1, data.Value<int>("parameterCount"));
            Assert.AreEqual(1, data.Value<int>("layerCount"));

            var layers = data["layers"] as JArray;
            Assert.IsNotNull(layers);
            Assert.AreEqual(1, layers.Count);
        }

        [Test]
        public void ControllerGetInfo_NotFound_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["action"] = "controller_get_info",
                ["controllerPath"] = "Assets/Nonexistent.controller"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
        }

        // =============================================================================
        // Controller: Assign
        // =============================================================================

        [Test]
        public void ControllerAssign_AddsAnimatorAndAssigns()
        {
            string controllerPath = $"{TempRoot}/AssignController_{Guid.NewGuid():N}.controller";
            AnimatorController.CreateAnimatorControllerAtPath(controllerPath);
            AssetDatabase.SaveAssets();

            var go = new GameObject("AnimTest_ControllerAssign");
            try
            {
                Assert.IsNull(go.GetComponent<Animator>());

                var paramsObj = new JObject
                {
                    ["action"] = "controller_assign",
                    ["controllerPath"] = controllerPath,
                    ["target"] = "AnimTest_ControllerAssign"
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), result.ToString());

                var animator = go.GetComponent<Animator>();
                Assert.IsNotNull(animator, "Should have added Animator component");
                Assert.IsNotNull(animator.runtimeAnimatorController, "Should have assigned controller");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        // =============================================================================
        // Clip: Set Vector Curve
        // =============================================================================

        [Test]
        public void ClipSetVectorCurve_Sets3Curves()
        {
            string clipPath = $"{TempRoot}/VectorClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "VectorClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_set_vector_curve",
                ["clipPath"] = clipPath,
                ["property"] = "localPosition",
                ["keys"] = new JArray(
                    new JObject { ["time"] = 0f, ["value"] = new JArray(0f, 1f, -10f) },
                    new JObject { ["time"] = 1f, ["value"] = new JArray(2f, 1f, -10f) }
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            var bindings = AnimationUtility.GetCurveBindings(clip);
            // clip.SetCurve doesn't populate EditorCurve bindings — it uses legacy runtime curves
            // Verify via the data response
            var data = result["data"] as JObject;
            Assert.IsNotNull(data);
            Assert.AreEqual(2, data.Value<int>("keyframeCount"));
            var curves = data["curves"] as JArray;
            Assert.IsNotNull(curves);
            Assert.AreEqual(3, curves.Count);
        }

        [Test]
        public void ClipSetVectorCurve_MissingProperty_ReturnsError()
        {
            string clipPath = $"{TempRoot}/NoPropertyClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "NoPropertyClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_set_vector_curve",
                ["clipPath"] = clipPath,
                ["keys"] = new JArray(
                    new JObject { ["time"] = 0f, ["value"] = new JArray(0f, 0f, 0f) }
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("property"));
        }

        [Test]
        public void ClipSetVectorCurve_InvalidValueFormat_ReturnsError()
        {
            string clipPath = $"{TempRoot}/BadValueClip_{Guid.NewGuid():N}.anim";
            var clip = new AnimationClip { name = "BadValueClip" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_set_vector_curve",
                ["clipPath"] = clipPath,
                ["property"] = "localPosition",
                ["keys"] = new JArray(
                    new JObject { ["time"] = 0f, ["value"] = new JArray(0f, 1f) } // Only 2 elements
                )
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("3-element"));
        }

        // =============================================================================
        // Clip: Create Preset
        // =============================================================================

        [Test]
        public void ClipCreatePreset_Bounce_CreatesClip()
        {
            string clipPath = $"{TempRoot}/BouncePreset_{Guid.NewGuid():N}.anim";
            var paramsObj = new JObject
            {
                ["action"] = "clip_create_preset",
                ["clipPath"] = clipPath,
                ["preset"] = "bounce",
                ["duration"] = 2.0f,
                ["amplitude"] = 0.5f,
                ["loop"] = true
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsTrue(result.Value<bool>("success"), result.ToString());

            var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
            Assert.IsNotNull(clip, "Bounce preset clip should exist");

            var settings = AnimationUtility.GetAnimationClipSettings(clip);
            Assert.IsTrue(settings.loopTime, "Should be looping");
        }

        [Test]
        public void ClipCreatePreset_AllPresetsCreateSuccessfully()
        {
            string[] presets = { "bounce", "rotate", "pulse", "fade", "shake", "hover", "spin" };
            foreach (var preset in presets)
            {
                string clipPath = $"{TempRoot}/{preset}Preset_{Guid.NewGuid():N}.anim";
                var paramsObj = new JObject
                {
                    ["action"] = "clip_create_preset",
                    ["clipPath"] = clipPath,
                    ["preset"] = preset
                };
                var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
                Assert.IsTrue(result.Value<bool>("success"), $"Preset '{preset}' failed: {result}");

                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(clipPath);
                Assert.IsNotNull(clip, $"Clip for preset '{preset}' should exist");
            }
        }

        [Test]
        public void ClipCreatePreset_InvalidPreset_ReturnsError()
        {
            string clipPath = $"{TempRoot}/BadPreset_{Guid.NewGuid():N}.anim";
            var paramsObj = new JObject
            {
                ["action"] = "clip_create_preset",
                ["clipPath"] = clipPath,
                ["preset"] = "nonexistent"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("Unknown preset"));
        }

        [Test]
        public void ClipCreatePreset_MissingPreset_ReturnsError()
        {
            string clipPath = $"{TempRoot}/NoPreset_{Guid.NewGuid():N}.anim";
            var paramsObj = new JObject
            {
                ["action"] = "clip_create_preset",
                ["clipPath"] = clipPath
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("preset"));
        }

        [Test]
        public void ClipCreatePreset_DuplicatePath_ReturnsError()
        {
            string clipPath = $"{TempRoot}/ExistingPreset.anim";
            var clip = new AnimationClip { name = "ExistingPreset" };
            AssetDatabase.CreateAsset(clip, clipPath);
            AssetDatabase.SaveAssets();

            var paramsObj = new JObject
            {
                ["action"] = "clip_create_preset",
                ["clipPath"] = clipPath,
                ["preset"] = "bounce"
            };
            var result = ToJObject(ManageAnimation.HandleCommand(paramsObj));
            Assert.IsFalse(result.Value<bool>("success"));
            Assert.That(result["message"].ToString(), Does.Contain("already exists"));
        }
    }
}
