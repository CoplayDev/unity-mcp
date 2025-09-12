using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools.MenuItems;
using System;
using System.Linq;

namespace MCPForUnityTests.Editor.Tools.MenuItems
{
    public class MenuItemsReaderTests
    {
        private static JObject ToJO(object o) => JObject.FromObject(o);

        [Test]
        public void List_NoSearch_ReturnsSuccessAndArray()
        {
            var res = MenuItemsReader.List(new JObject());
            var jo = ToJO(res);
            Assert.IsTrue((bool)jo["success"], "Expected success true");
            Assert.IsNotNull(jo["data"], "Expected data field present");
            Assert.AreEqual(JTokenType.Array, jo["data"].Type, "Expected data to be an array");

            // Validate list is sorted ascending when there are multiple items
            var arr = (JArray)jo["data"];
            if (arr.Count >= 2)
            {
                var original = arr.Select(t => (string)t).ToList();
                var sorted = original.OrderBy(s => s, StringComparer.Ordinal).ToList();
                CollectionAssert.AreEqual(sorted, original, "Expected menu items to be sorted ascending");
            }
        }

        [Test]
        public void List_SearchNoMatch_ReturnsEmpty()
        {
            var res = MenuItemsReader.List(new JObject { ["search"] = "___unlikely___term___" });
            var jo = ToJO(res);
            Assert.IsTrue((bool)jo["success"], "Expected success true");
            Assert.AreEqual(JTokenType.Array, jo["data"].Type, "Expected data to be an array");
            Assert.AreEqual(0, jo["data"].Count(), "Expected no results for unlikely search term");
        }

        [Test]
        public void List_SearchMatchesExistingItem_ReturnsContainingItem()
        {
            // Get the full list first
            var listRes = MenuItemsReader.List(new JObject());
            var listJo = ToJO(listRes);
            if (listJo["data"] is JArray arr && arr.Count > 0)
            {
                var first = (string)arr[0];
                // Use a mid-substring (case-insensitive) to avoid edge cases
                var term = first.Length > 4 ? first.Substring(1, Math.Min(3, first.Length - 2)) : first;
                term = term.ToLowerInvariant();

                var res = MenuItemsReader.List(new JObject { ["search"] = term });
                var jo = ToJO(res);
                Assert.IsTrue((bool)jo["success"], "Expected success true");
                Assert.AreEqual(JTokenType.Array, jo["data"].Type, "Expected data to be an array");
                // Expect at least the original item to be present
                var names = ((JArray)jo["data"]).Select(t => (string)t).ToList();
                CollectionAssert.Contains(names, first, "Expected search results to include the sampled item");
            }
            else
            {
                Assert.Pass("No menu items available to perform a content-based search assertion.");
            }
        }

        [Test]
        public void Exists_MissingParam_ReturnsError()
        {
            var res = MenuItemsReader.Exists(new JObject());
            var jo = ToJO(res);
            Assert.IsFalse((bool)jo["success"], "Expected success false");
            StringAssert.Contains("Required parameter", (string)jo["error"]);
        }

        [Test]
        public void Exists_Bogus_ReturnsFalse()
        {
            var res = MenuItemsReader.Exists(new JObject { ["menuPath"] = "Nonexistent/Menu/___unlikely___" });
            var jo = ToJO(res);
            Assert.IsTrue((bool)jo["success"], "Expected success true");
            Assert.IsNotNull(jo["data"], "Expected data field present");
            Assert.IsFalse((bool)jo["data"]["exists"], "Expected exists false for bogus menu path");
        }
    }
}
