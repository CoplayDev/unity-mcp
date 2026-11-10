using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Helpers;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Built-in components back public properties with differently named native fields
    /// (SpriteRenderer.sprite is serialized as m_Sprite). Object-form values go through the
    /// SerializedProperty path, which must still find those fields (#1413).
    /// </summary>
    public class ComponentOpsNativeFieldTests
    {
        private GameObject testGo;
        private Texture2D texture;
        private Sprite sprite;

        [SetUp]
        public void SetUp()
        {
            testGo = new GameObject("NativeFieldTestGO");
            texture = new Texture2D(4, 4);
            sprite = Sprite.Create(texture, new Rect(0, 0, 4, 4), Vector2.zero);
        }

        [TearDown]
        public void TearDown()
        {
            if (testGo != null) Object.DestroyImmediate(testGo);
            if (sprite != null) Object.DestroyImmediate(sprite);
            if (texture != null) Object.DestroyImmediate(texture);
        }

        [Test]
        public void SetProperty_SpriteRendererSprite_ObjectForm_SetsNativeField()
        {
            var renderer = testGo.AddComponent<SpriteRenderer>();
            var value = new JObject { ["instanceID"] = sprite.GetInstanceID() };

            bool ok = ComponentOps.SetProperty(renderer, "sprite", value, out string error);

            Assert.IsTrue(ok, $"SetProperty should succeed, got error: {error}");
            Assert.AreSame(sprite, renderer.sprite);
        }

        [Test]
        public void SetProperty_SpriteRendererSprite_LowercasePrefix_SetsNativeField()
        {
            var renderer = testGo.AddComponent<SpriteRenderer>();
            var value = new JObject { ["instanceID"] = sprite.GetInstanceID() };

            bool ok = ComponentOps.SetProperty(renderer, "m_sprite", value, out string error);

            Assert.IsTrue(ok, $"SetProperty should succeed, got error: {error}");
            Assert.AreSame(sprite, renderer.sprite);
        }

        [Test]
        public void SetProperty_AmbiguousAlias_ObjectForm_FailsWithoutWriting()
        {
            var component = testGo.AddComponent<AmbiguousFieldsBehaviour>();
            var value = new JObject { ["instanceID"] = testGo.GetInstanceID() };

            bool ok = ComponentOps.SetProperty(component, "target", value, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("more than one", error);
            Assert.IsNull(component.TargetUnderscore);
            Assert.IsNull(component.TargetPrefixed);
        }

        [Test]
        public void SetProperty_AmbiguousAlias_ExactName_StillWorks()
        {
            var component = testGo.AddComponent<AmbiguousFieldsBehaviour>();
            var value = new JObject { ["instanceID"] = testGo.GetInstanceID() };

            bool ok = ComponentOps.SetProperty(component, "m_Target", value, out string error);

            Assert.IsTrue(ok, $"SetProperty should succeed, got error: {error}");
            Assert.AreSame(testGo, component.TargetPrefixed);
            Assert.IsNull(component.TargetUnderscore);
        }

        [Test]
        public void SetProperty_UnknownProperty_ObjectForm_StillFails()
        {
            var renderer = testGo.AddComponent<SpriteRenderer>();
            var value = new JObject { ["instanceID"] = sprite.GetInstanceID() };

            bool ok = ComponentOps.SetProperty(renderer, "notARealProperty", value, out string error);

            Assert.IsFalse(ok);
            StringAssert.Contains("not found", error);
        }
    }

    /// <summary>Two serialized fields that both normalize to "target".</summary>
    public class AmbiguousFieldsBehaviour : MonoBehaviour
    {
        [SerializeField] private GameObject target_;
        [SerializeField] private GameObject m_Target;

        public GameObject TargetUnderscore => target_;
        public GameObject TargetPrefixed => m_Target;
    }
}
