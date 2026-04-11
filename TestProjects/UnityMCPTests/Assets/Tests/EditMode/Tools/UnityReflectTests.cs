using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    [TestFixture]
    public class UnityReflectTests
    {
        private static JObject Invoke(string action, JObject extraParams = null)
        {
            var p = extraParams ?? new JObject();
            p["action"] = action;
            var result = UnityReflect.HandleCommand(p);
            return JObject.FromObject(result);
        }

        // ── get_type ────────────────────────────────────────────────

        [Test]
        public void GetType_Transform_ReturnsFound()
        {
            var jo = Invoke("get_type", new JObject { ["class_name"] = "UnityEngine.Transform" });

            Assert.IsTrue((bool)jo["success"]);
            var data = jo["data"];
            Assert.IsTrue((bool)data["found"]);
            Assert.AreEqual("UnityEngine.Transform", (string)data["full_name"]);
        }

        [Test]
        public void GetType_ShortName_ResolvesNamespace()
        {
            var jo = Invoke("get_type", new JObject { ["class_name"] = "NavMeshAgent" });

            Assert.IsTrue((bool)jo["success"]);
            var data = jo["data"];
            Assert.IsTrue((bool)data["found"]);
            Assert.AreEqual("UnityEngine.AI.NavMeshAgent", (string)data["full_name"]);
        }

        [Test]
        public void GetType_HasMembers()
        {
            var jo = Invoke("get_type", new JObject { ["class_name"] = "Camera" });

            Assert.IsTrue((bool)jo["success"]);
            var data = jo["data"];
            Assert.IsTrue((bool)data["found"]);

            var members = data["members"];
            Assert.IsNotNull(members, "members should be present");

            var methods = (JArray)members["methods"];
            Assert.IsNotNull(methods, "methods array should be present");
            Assert.Greater(methods.Count, 0, "Camera should have methods");

            var properties = (JArray)members["properties"];
            Assert.IsNotNull(properties, "properties array should be present");
            Assert.Greater(properties.Count, 0, "Camera should have properties");
        }

        [Test]
        public void GetType_Ambiguous_ReturnsMultiple()
        {
            var jo = Invoke("get_type", new JObject { ["class_name"] = "Button" });

            Assert.IsTrue((bool)jo["success"]);
            var data = jo["data"];
            Assert.IsTrue((bool)data["ambiguous"], "Button should be ambiguous (UnityEngine.UI.Button vs UnityEngine.UIElements.Button)");

            var matches = (JArray)data["matches"];
            Assert.IsNotNull(matches, "matches array should be present");
            Assert.Greater(matches.Count, 1, "Should have multiple matches");
        }

        [Test]
        public void GetType_NotFound_ReturnsFalse()
        {
            var jo = Invoke("get_type", new JObject { ["class_name"] = "TotallyFakeClass12345" });

            Assert.IsTrue((bool)jo["success"], "Should be a SuccessResponse even for not-found");
            var data = jo["data"];
            Assert.IsFalse((bool)data["found"]);
        }

        [Test]
        public void GetType_MissingClassName_ReturnsError()
        {
            var jo = Invoke("get_type", new JObject());

            Assert.IsFalse((bool)jo["success"]);
            Assert.IsNotNull(jo["error"], "Should have an error message");
        }

        // ── get_member ──────────────────────────────────────────────

        [Test]
        public void GetMember_Method_ReturnsSignature()
        {
            var jo = Invoke("get_member", new JObject
            {
                ["class_name"] = "Physics",
                ["member_name"] = "Raycast"
            });

            Assert.IsTrue((bool)jo["success"]);
            var data = jo["data"];
            Assert.IsTrue((bool)data["found"]);
            Assert.AreEqual("method", (string)data["member_type"]);
            Assert.Greater((int)data["overload_count"], 0, "Raycast should have overloads");

            var overloads = (JArray)data["overloads"];
            Assert.IsNotNull(overloads);
            Assert.Greater(overloads.Count, 0);
        }

        [Test]
        public void GetMember_Property_ReturnsPropertyInfo()
        {
            var jo = Invoke("get_member", new JObject
            {
                ["class_name"] = "UnityEngine.Transform",
                ["member_name"] = "position"
            });

            Assert.IsTrue((bool)jo["success"]);
            var data = jo["data"];
            Assert.IsTrue((bool)data["found"]);
            Assert.AreEqual("property", (string)data["member_type"]);
            Assert.IsNotNull(data["property_type"]);
        }

        [Test]
        public void GetMember_NotFound_ReturnsFalse()
        {
            var jo = Invoke("get_member", new JObject
            {
                ["class_name"] = "Physics",
                ["member_name"] = "TotallyFakeMethod"
            });

            Assert.IsTrue((bool)jo["success"], "Should be a SuccessResponse even for not-found member");
            var data = jo["data"];
            Assert.IsFalse((bool)data["found"]);
        }

        // ── search ──────────────────────────────────────────────────

        [Test]
        public void Search_NavMesh_FindsMultipleTypes()
        {
            var jo = Invoke("search", new JObject
            {
                ["query"] = "NavMesh",
                ["scope"] = "unity"
            });

            Assert.IsTrue((bool)jo["success"]);
            var data = jo["data"];
            Assert.Greater((int)data["count"], 1, "NavMesh should match multiple types");
        }

        [Test]
        public void Search_ExactMatch_RankedFirst()
        {
            var jo = Invoke("search", new JObject
            {
                ["query"] = "Camera",
                ["scope"] = "unity"
            });

            Assert.IsTrue((bool)jo["success"]);
            var data = jo["data"];
            Assert.Greater((int)data["count"], 0);

            var results = (JArray)data["results"];
            var firstFullName = (string)results[0]["full_name"];
            Assert.That(firstFullName, Does.EndWith(".Camera"),
                "First result should be an exact match ending with '.Camera'");
        }

        [Test]
        public void Search_NoResults_ReturnsZeroCount()
        {
            var jo = Invoke("search", new JObject
            {
                ["query"] = "ZzzNonexistentType999",
                ["scope"] = "unity"
            });

            Assert.IsTrue((bool)jo["success"], "Should be a SuccessResponse even with no results");
            var data = jo["data"];
            Assert.AreEqual(0, (int)data["count"]);
        }

        // ── generic types ───────────────────────────────────────────

        [Test]
        public void GetType_GenericList_Resolves()
        {
            var jo = Invoke("get_type", new JObject { ["class_name"] = "List<T>" });

            Assert.IsTrue((bool)jo["success"]);
            var data = jo["data"];
            Assert.IsTrue((bool)data["found"], "List<T> should resolve via generic normalization");
        }

        [Test]
        public void GetType_GenericDictionary_Resolves()
        {
            var jo = Invoke("get_type", new JObject { ["class_name"] = "Dictionary<TKey, TValue>" });
            Assert.IsTrue((bool)jo["success"]);
            var data = jo["data"];
            Assert.IsTrue((bool)data["found"], "Dictionary<TKey, TValue> should resolve via generic normalization");
        }
    }
}
