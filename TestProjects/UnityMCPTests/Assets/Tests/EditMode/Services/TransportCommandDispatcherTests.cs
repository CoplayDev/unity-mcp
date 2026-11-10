using System.Collections;
using System.Text.RegularExpressions;
using System.Threading;
using Newtonsoft.Json.Linq;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;
using MCPForUnity.Editor.Services.Transport;

namespace MCPForUnityTests.Editor.Services
{
    [TestFixture]
    public class TransportCommandDispatcherTests
    {
        [UnityTest]
        public IEnumerator UnknownCommand_IsAnsweredWithoutAnErrorInTheConsole()
        {
            // A server or CLI newer than the package sends commands this package does not have.
            // That reached the console as a red error with a stack trace, as if the Editor broke.
            LogAssert.Expect(LogType.Warning, new Regex("no_such_command.*update the package"));

            var reply = TransportCommandDispatcher.ExecuteCommandJsonAsync(
                "{\"type\":\"no_such_command\",\"params\":{}}", CancellationToken.None);
            for (int frame = 0; frame < 600 && !reply.IsCompleted; frame++)
            {
                yield return null;
            }

            Assert.IsTrue(reply.IsCompleted, "the dispatcher never answered");
            var response = JObject.Parse(reply.Result);
            Assert.AreEqual("error", (string)response["status"]);
            StringAssert.Contains("'no_such_command'", (string)response["error"]);
            Assert.AreEqual("no_such_command", (string)response["command"]);
            Assert.IsNull((string)response["stackTrace"]);
        }
    }
}
