using NUnit.Framework;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using UnityEditor;
using UnityEngine;

namespace MCPForUnity.Tests.EditMode.Tools
{
    [TestFixture]
    public class ManageEditorUndoTests
    {
        [Test]
        public void Undo_ReturnsSuccess()
        {
            var p = new JObject { ["action"] = "undo" };
            var result = ManageEditor.HandleCommand(p);
            var r = result as JObject ?? JObject.FromObject(result);
            Assert.IsTrue(r.Value<bool>("success"), r.ToString());
        }

        [Test]
        public void Redo_ReturnsSuccess()
        {
            var p = new JObject { ["action"] = "redo" };
            var result = ManageEditor.HandleCommand(p);
            var r = result as JObject ?? JObject.FromObject(result);
            Assert.IsTrue(r.Value<bool>("success"), r.ToString());
        }

        [Test]
        public void Undo_AfterRecordedChange_RevertsChange()
        {
            var go = new GameObject("UndoTestGO");
            try
            {
                Undo.RecordObject(go, "Rename UndoTestGO");
                go.name = "RenamedGO";
                Undo.FlushUndoRecordObjects();

                var p = new JObject { ["action"] = "undo" };
                ManageEditor.HandleCommand(p);
                Assert.AreEqual("UndoTestGO", go.name, "Name should revert after undo");
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
