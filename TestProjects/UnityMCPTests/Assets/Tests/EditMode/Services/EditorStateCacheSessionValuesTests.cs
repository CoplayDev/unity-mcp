using NUnit.Framework;
using UnityEditor;
using MCPForUnity.Editor.Services;

namespace MCPForUnityTests.Editor.Services
{
    [TestFixture]
    public class EditorStateCacheSessionValuesTests
    {
        private const string Key = "MCPForUnityTests.EditorStateCache.SessionUnixMs";

        [SetUp]
        public void SetUp() => SessionState.EraseString(Key);

        [TearDown]
        public void TearDown() => SessionState.EraseString(Key);

        [Test]
        public void SessionUnixMs_RoundTripsValueBeyondInt32()
        {
            // A unix-ms timestamp does not fit an int; SessionState has no long
            // overload, so the value goes through a string and must come back exact.
            const long value = 1788652878150L;
            Assert.Greater(value, int.MaxValue, "test value must exceed what SessionState.SetInt could hold");

            EditorStateCache.SetSessionUnixMs(Key, value);

            Assert.AreEqual(value, EditorStateCache.GetSessionUnixMs(Key));
        }

        [Test]
        public void GetSessionUnixMs_UnsetKey_ReturnsNull()
        {
            Assert.IsNull(EditorStateCache.GetSessionUnixMs(Key));
        }

        [TestCase("not-a-number")]
        [TestCase("1788652878150.5")]
        [TestCase("1,788,652,878,150")]
        public void GetSessionUnixMs_MalformedValue_ReturnsNull(string raw)
        {
            SessionState.SetString(Key, raw);

            Assert.IsNull(EditorStateCache.GetSessionUnixMs(Key));
        }
    }
}
