using MCPForUnity.Editor.Security;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    public class SecretRedactorTests
    {
        [Test]
        public void Scrub_RedactsBearerToken()
        {
            string s = SecretRedactor.Scrub("Authorization: Bearer tsk_abc123XYZ");
            StringAssert.DoesNotContain("tsk_abc123XYZ", s);
            StringAssert.Contains("REDACTED", s);
        }

        [Test]
        public void Scrub_RedactsKeyAndTokenSchemes()
        {
            StringAssert.DoesNotContain("falkey123456", SecretRedactor.Scrub("Authorization: Key falkey123456"));
            StringAssert.DoesNotContain("sktoken987654", SecretRedactor.Scrub("Authorization: Token sktoken987654"));
        }

        [Test]
        public void Scrub_WithExplicitSecret_RedactsThatValue()
        {
            string s = SecretRedactor.Scrub("error: key sk-supersecret rejected", "sk-supersecret");
            StringAssert.DoesNotContain("sk-supersecret", s);
        }

        [Test]
        public void Scrub_LeavesNormalTextAlone()
        {
            Assert.AreEqual("hello world", SecretRedactor.Scrub("hello world"));
        }
    }
}
