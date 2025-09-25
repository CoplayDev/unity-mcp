using System;
using Newtonsoft.Json;
using NUnit.Framework;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class CommandRegistryTests
    {
        [Test]
        public void GetHandler_ThrowException_ForUnknownCommand()
        {
            var unknown = "HandleDoesNotExist";
            try
            {
                var handler = CommandRegistry.GetHandler(unknown);
                Assert.Fail("Should throw InvalidOperation for unknown handler.");
            }
            catch (InvalidOperationException)
            {

            }
            catch
            {
                Assert.Fail("Should throw InvalidOperation for unknown handler.");
            }
        }

        [Test]
        public void GetHandler_ReturnsManageGameObjectHandler()
        {
            var handler = CommandRegistry.GetHandler("manage_gameobject");
            Assert.IsNotNull(handler, "Expected a handler for manage_gameobject.");

            var methodInfo = handler.Method;
            Assert.AreEqual("HandleCommand", methodInfo.Name, "Handler method name should be HandleCommand.");
            Assert.AreEqual(typeof(ManageGameObject), methodInfo.DeclaringType, "Handler should be declared on ManageGameObject.");
            Assert.IsNull(handler.Target, "Handler should be a static method (no target instance).");
        }
    }
}
