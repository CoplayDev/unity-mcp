using System;
using System.Collections.Generic;
using System.Globalization;
using System.Security.Cryptography;
using System.Text;

namespace MCPForUnity.Editor.Services.AssetGen.Providers
{
    /// <summary>
    /// Implements Tencent Cloud's TC3-HMAC-SHA256 request signing (signature method v3) for the
    /// fixed two-header canonical form used by the Hunyuan 3D API: only <c>content-type</c> and
    /// <c>host</c> are signed, the canonical URI is "/", and the query string is empty. Produces
    /// the full header set (Authorization, Host, Content-Type, X-TC-Action/Timestamp/Version).
    /// Secrets stay in memory and never reach a log.
    /// </summary>
    public static class TencentCloud3Signer
    {
        private const string Algorithm = "TC3-HMAC-SHA256";
        private const string ContentType = "application/json; charset=utf-8";

        public static Dictionary<string, string> SignedHeaders(
            string service, string host, string action, string version,
            string payloadJson, string secretId, string secretKey, DateTimeOffset utcNow)
        {
            payloadJson ??= string.Empty;
            long unixTs = utcNow.ToUnixTimeSeconds();
            string date = utcNow.UtcDateTime.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

            // 1. Canonical request (two signed headers: content-type;host).
            string canonicalRequest =
                "POST\n" +
                "/\n" +
                "\n" +
                "content-type:" + ContentType + "\n" +
                "host:" + host + "\n" +
                "\n" +
                "content-type;host\n" +
                Hex(Sha256(payloadJson));

            // 2. String to sign.
            string credentialScope = date + "/" + service + "/tc3_request";
            string stringToSign =
                Algorithm + "\n" +
                unixTs.ToString(CultureInfo.InvariantCulture) + "\n" +
                credentialScope + "\n" +
                Hex(Sha256(canonicalRequest));

            // 3. Derive the signing key and sign.
            byte[] secretDate = HmacSha256(Utf8("TC3" + secretKey), date);
            byte[] secretService = HmacSha256(secretDate, service);
            byte[] secretSigning = HmacSha256(secretService, "tc3_request");
            string signature = Hex(HmacSha256(secretSigning, stringToSign));

            // 4. Authorization header.
            string authorization =
                Algorithm +
                " Credential=" + secretId + "/" + credentialScope +
                ", SignedHeaders=content-type;host" +
                ", Signature=" + signature;

            return new Dictionary<string, string>
            {
                ["Authorization"] = authorization,
                ["Host"] = host,
                ["Content-Type"] = ContentType,
                ["X-TC-Action"] = action,
                ["X-TC-Timestamp"] = unixTs.ToString(CultureInfo.InvariantCulture),
                ["X-TC-Version"] = version,
            };
        }

        private static byte[] Utf8(string s) => Encoding.UTF8.GetBytes(s ?? string.Empty);

        private static byte[] Sha256(string s)
        {
            using (var sha = SHA256.Create())
                return sha.ComputeHash(Utf8(s));
        }

        private static byte[] HmacSha256(byte[] key, string msg)
        {
            using (var mac = new HMACSHA256(key))
                return mac.ComputeHash(Utf8(msg));
        }

        private static string Hex(byte[] bytes)
        {
            var sb = new StringBuilder(bytes.Length * 2);
            foreach (byte b in bytes) sb.Append(b.ToString("x2", CultureInfo.InvariantCulture));
            return sb.ToString();
        }
    }
}
