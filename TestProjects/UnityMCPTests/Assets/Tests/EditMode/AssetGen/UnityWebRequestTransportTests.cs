using MCPForUnity.Editor.Services.AssetGen.Http;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// H1: an auth-bearing request must not follow redirects (UnityWebRequest re-sends the
    /// Authorization header to a 3xx target by default). We can't drive a real redirect headlessly,
    /// so we test the decision helper that gates redirectLimit = 0.
    /// </summary>
    public class UnityWebRequestTransportTests
    {
        [Test]
        public void CarriesAuth_TrueWhenAuthorizationHeaderPresent()
        {
            var spec = new HttpRequestSpec { Method = "GET", Url = "https://queue.fal.run/x" };
            spec.Headers["Authorization"] = "Key secret123";

            Assert.IsTrue(UnityWebRequestTransport.CarriesAuth(spec));
        }

        [Test]
        public void CarriesAuth_CaseInsensitiveHeaderKey()
        {
            var spec = new HttpRequestSpec { Method = "GET", Url = "https://queue.fal.run/x" };
            spec.Headers["authorization"] = "Bearer secret123";

            Assert.IsTrue(UnityWebRequestTransport.CarriesAuth(spec));
        }

        [Test]
        public void CarriesAuth_FalseWhenNoAuthorizationHeader()
        {
            var spec = new HttpRequestSpec { Method = "GET", Url = "https://cdn.example.com/a.wav" };

            Assert.IsFalse(UnityWebRequestTransport.CarriesAuth(spec));
        }
    }
}
