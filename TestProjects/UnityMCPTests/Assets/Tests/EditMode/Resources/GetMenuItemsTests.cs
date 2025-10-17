using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Resources.MenuItems;
using System;
using System.Linq;

namespace MCPForUnityTests.Editor.Resources.MenuItems
{
    public class GetMenuItemsTests
    {
        private static JObject ToJO(object o) => JObject.FromObject(o);

        [Test]
        public void NoSearch_ReturnsSuccessAndArray()
        {
            var res = GetMenuItems.HandleCommand(new JObject { ["search"] = "", ["refresh"] = false });
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
        public void SearchNoMatch_ReturnsEmpty()
        {
            var res = GetMenuItems.HandleCommand(new JObject { ["search"] = "___unlikely___term___" });
            var jo = ToJO(res);
            Assert.IsTrue((bool)jo["success"], "Expected success true");
            Assert.AreEqual(JTokenType.Array, jo["data"].Type, "Expected data to be an array");
            Assert.AreEqual(0, jo["data"].Count(), "Expected no results for unlikely search term");
        }

        [Test]
        public void SearchMatchesExistingItem_ReturnsContainingItem()
        {
            // Get the full list first
            var listRes = GetMenuItems.HandleCommand(new JObject { ["search"] = "", ["refresh"] = false });
            var listJo = ToJO(listRes);
            if (listJo["data"] is JArray arr && arr.Count > 0)
            {
                var first = (string)arr[0];
                // Use a mid-substring (case-insensitive) to avoid edge cases
                var term = first.Length > 4 ? first.Substring(1, Math.Min(3, first.Length - 2)) : first;
                term = term.ToLowerInvariant();

                var res = GetMenuItems.HandleCommand(new JObject { ["search"] = term, ["refresh"] = false });
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
    }
}
