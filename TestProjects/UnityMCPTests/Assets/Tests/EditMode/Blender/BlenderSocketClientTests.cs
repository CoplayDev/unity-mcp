using System.Text;
using MCPForUnity.Editor.Services.Blender;
using Newtonsoft.Json.Linq;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.Blender
{
    /// <summary>
    /// Covers the framing-free protocol core (parse-when-complete + status unwrap) without a socket.
    /// </summary>
    public class BlenderSocketClientTests
    {
        private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

        [Test]
        public void TryParseResponse_IsFalse_ForAPartialPrefix()
        {
            byte[] buf = Bytes("{\"status\": \"success\", \"result\": {\"name\": \"Sce");
            Assert.IsFalse(BlenderSocketClient.TryParseResponse(buf, buf.Length, out JObject parsed));
            Assert.IsNull(parsed);
        }

        [Test]
        public void TryParseResponse_IsTrue_OnceTheObjectIsComplete()
        {
            byte[] buf = Bytes("{\"status\": \"success\", \"result\": {\"name\": \"Scene\", \"object_count\": 3}}");
            Assert.IsTrue(BlenderSocketClient.TryParseResponse(buf, buf.Length, out JObject parsed));
            Assert.AreEqual("Scene", (string)parsed["result"]["name"]);
        }

        [Test]
        public void TryParseResponse_HonoursLength_NotBufferCapacity()
        {
            string json = "{\"status\": \"success\", \"result\": 1}";
            byte[] buf = new byte[256];
            byte[] src = Bytes(json);
            src.CopyTo(buf, 0);
            Assert.IsTrue(BlenderSocketClient.TryParseResponse(buf, src.Length, out JObject parsed));
            Assert.AreEqual(1, (int)parsed["result"]);
        }

        [Test]
        public void Unwrap_ReturnsResult_OnSuccess()
        {
            var response = JObject.Parse("{\"status\": \"success\", \"result\": {\"executed\": true}}");
            JToken result = BlenderSocketClient.Unwrap(response, "execute_code");
            Assert.IsTrue((bool)result["executed"]);
        }

        [Test]
        public void Unwrap_Throws_WithAddonMessage_OnError()
        {
            var response = JObject.Parse("{\"status\": \"error\", \"message\": \"boom\"}");
            var ex = Assert.Throws<BlenderCommandException>(() => BlenderSocketClient.Unwrap(response, "get_scene_info"));
            StringAssert.Contains("boom", ex.Message);
            StringAssert.Contains("get_scene_info", ex.Message);
        }
    }
}
