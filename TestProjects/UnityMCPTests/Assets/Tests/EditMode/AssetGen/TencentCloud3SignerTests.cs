using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using MCPForUnity.Editor.Services.AssetGen.Providers;
using NUnit.Framework;

namespace MCPForUnityTests.Editor.AssetGen
{
    /// <summary>
    /// Known-answer + determinism coverage for the TC3-HMAC-SHA256 signer.
    ///
    /// Provenance of the pinned signature: Tencent's published "signature v3" worked example
    /// (SecretId AKIDz8krbsJ5yKBZQpn74WFkmLPx3*******, SecretKey Gu5t9xGARNpq86cd98joQYCN3*******,
    /// timestamp 1551113065, service cvm, action DescribeInstances) is fully reproducible because
    /// the example uses those masked strings *verbatim* as the credential bytes. The live doc's
    /// current worked example signs three headers (content-type;host;x-tc-action); this signer
    /// (and the Hunyuan API it serves) signs the two-header canonical form (content-type;host),
    /// so the doc's published Signature does not apply directly. The value pinned below was
    /// computed independently for the two-header form with a Python stdlib hashlib/hmac oracle
    /// over the identical inputs — i.e. a cross-implementation known answer, not this code's own
    /// output. A Tencent-published two-header KAT remains pending.
    /// </summary>
    public class TencentCloud3SignerTests
    {
        private const string SecretId = "AKIDz8krbsJ5yKBZQpn74WFkmLPx3*******";
        private const string SecretKey = "Gu5t9xGARNpq86cd98joQYCN3*******";
        private const string Service = "cvm";
        private const string Host = "cvm.tencentcloudapi.com";
        private const string Action = "DescribeInstances";
        private const string Version = "2017-03-12";
        private const string Payload = "{\"Limit\": 1, \"Filters\": [{\"Name\": \"instance-name\", \"Values\": [\"未命名\"]}]}";
        private const long Timestamp = 1551113065L; // 2019-02-25T23:24:25Z

        // Cross-implementation reference (Python hashlib/hmac) for the two-header canonical form.
        private const string ExpectedSignature = "fb28de4cca534722afe861e8bc827bcd4f2a603df19925955e2af50d3a3f264d";

        private static Dictionary<string, string> Sign()
            => TencentCloud3Signer.SignedHeaders(
                Service, Host, Action, Version, Payload, SecretId, SecretKey,
                DateTimeOffset.FromUnixTimeSeconds(Timestamp));

        [Test]
        public void KnownAnswer_ReproducesReferenceSignature()
        {
            Dictionary<string, string> headers = Sign();
            string auth = headers["Authorization"];

            StringAssert.StartsWith(
                "TC3-HMAC-SHA256 Credential=" + SecretId + "/2019-02-25/cvm/tc3_request, " +
                "SignedHeaders=content-type;host, Signature=", auth);
            StringAssert.EndsWith("Signature=" + ExpectedSignature, auth);
        }

        [Test]
        public void EmitsExpectedHeaderSet()
        {
            Dictionary<string, string> headers = Sign();
            Assert.AreEqual(Host, headers["Host"]);
            Assert.AreEqual("application/json; charset=utf-8", headers["Content-Type"]);
            Assert.AreEqual(Action, headers["X-TC-Action"]);
            Assert.AreEqual(Timestamp.ToString(), headers["X-TC-Timestamp"]);
            Assert.AreEqual(Version, headers["X-TC-Version"]);
        }

        [Test]
        public void Signature_IsDeterministic_64LowerHex()
        {
            Dictionary<string, string> a = Sign();
            Dictionary<string, string> b = Sign();
            Assert.AreEqual(a["Authorization"], b["Authorization"], "same inputs must produce the same signature");

            string sig = Regex.Match(a["Authorization"], "Signature=([0-9a-f]+)$").Groups[1].Value;
            Assert.AreEqual(64, sig.Length, "SHA256 hex signature is 64 chars");
            Assert.IsTrue(Regex.IsMatch(sig, "^[0-9a-f]{64}$"), "signature must be lowercase hex");
        }
    }
}
