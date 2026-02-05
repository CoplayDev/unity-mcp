using NUnit.Framework;
using MCPForUnity.Editor.Helpers;
using System.Collections.Generic;

namespace MCPForUnityTests.Editor.Helpers
{
    /// <summary>
    /// Tests for the HostAddress helper class.
    /// </summary>
    public class HostAddressTests
    {
        #region IsExplicitIPv4 Tests

        [Test]
        public void IsExplicitIPv4_ValidIPv4_ReturnsTrue()
        {
            Assert.IsTrue(HostAddress.IsExplicitIPv4("127.0.0.1"), "Loopback");
            Assert.IsTrue(HostAddress.IsExplicitIPv4("192.168.1.1"), "Private");
            Assert.IsTrue(HostAddress.IsExplicitIPv4("10.0.0.1"), "Class A private");
            Assert.IsTrue(HostAddress.IsExplicitIPv4("0.0.0.0"), "Wildcard");
            Assert.IsTrue(HostAddress.IsExplicitIPv4("255.255.255.255"), "Broadcast");
        }

        [Test]
        public void IsExplicitIPv4_InvalidIPv4_ReturnsFalse()
        {
            Assert.IsFalse(HostAddress.IsExplicitIPv4("256.0.0.1"), "Octet > 255");
            Assert.IsFalse(HostAddress.IsExplicitIPv4("192.168.1"), "Missing octet");
            Assert.IsFalse(HostAddress.IsExplicitIPv4("192.168.1.1.1"), "Extra octet");
            Assert.IsFalse(HostAddress.IsExplicitIPv4("192.168.1.a"), "Non-numeric");
            Assert.IsFalse(HostAddress.IsExplicitIPv4("localhost"), "Hostname");
            Assert.IsFalse(HostAddress.IsExplicitIPv4("::1"), "IPv6");
        }

        [Test]
        public void IsExplicitIPv4_NullOrEmpty_ReturnsFalse()
        {
            Assert.IsFalse(HostAddress.IsExplicitIPv4(null), "Null");
            Assert.IsFalse(HostAddress.IsExplicitIPv4(""), "Empty");
            Assert.IsFalse(HostAddress.IsExplicitIPv4("   "), "Whitespace");
        }

        #endregion

        #region IsExplicitIPv6 Tests

        [Test]
        public void IsExplicitIPv6_StandardIPv6_ReturnsTrue()
        {
            Assert.IsTrue(HostAddress.IsExplicitIPv6("::1"), "Loopback");
            Assert.IsTrue(HostAddress.IsExplicitIPv6("::"), "Unspecified");
            Assert.IsTrue(HostAddress.IsExplicitIPv6("fe80::1"), "Link-local");
            Assert.IsTrue(HostAddress.IsExplicitIPv6("2001:db8::1"), "Global unicast");
            Assert.IsTrue(HostAddress.IsExplicitIPv6("2001:0db8:85a3:0000:0000:8a2e:0370:7334"), "Full format");
        }

        [Test]
        public void IsExplicitIPv6_IPv4MappedIPv6_ReturnsTrue()
        {
            Assert.IsTrue(HostAddress.IsExplicitIPv6("::ffff:127.0.0.1"), "IPv4-mapped loopback");
            Assert.IsTrue(HostAddress.IsExplicitIPv6("::ffff:192.168.1.1"), "IPv4-mapped private");
            Assert.IsTrue(HostAddress.IsExplicitIPv6("::ffff:0.0.0.0"), "IPv4-mapped wildcard");
        }

        [Test]
        public void IsExplicitIPv6_IPv4CompatibleIPv6_ReturnsTrue()
        {
            Assert.IsTrue(HostAddress.IsExplicitIPv6("::127.0.0.1"), "IPv4-compatible loopback");
            Assert.IsTrue(HostAddress.IsExplicitIPv6("::192.168.1.1"), "IPv4-compatible private");
        }

        [Test]
        public void IsExplicitIPv6_WithZoneId_ReturnsTrue()
        {
            Assert.IsTrue(HostAddress.IsExplicitIPv6("fe80::1%eth0"), "With zone ID");
            Assert.IsTrue(HostAddress.IsExplicitIPv6("fe80::1%12"), "With numeric zone ID");
        }

        [Test]
        public void IsExplicitIPv6_IPv4_ReturnsFalse()
        {
            Assert.IsFalse(HostAddress.IsExplicitIPv6("127.0.0.1"), "IPv4 loopback");
            Assert.IsFalse(HostAddress.IsExplicitIPv6("192.168.1.1"), "IPv4 private");
        }

        [Test]
        public void IsExplicitIPv6_Hostname_ReturnsFalse()
        {
            Assert.IsFalse(HostAddress.IsExplicitIPv6("localhost"), "localhost");
            Assert.IsFalse(HostAddress.IsExplicitIPv6("example.com"), "example.com");
        }

        [Test]
        public void IsExplicitIPv6_NullOrEmpty_ReturnsFalse()
        {
            Assert.IsFalse(HostAddress.IsExplicitIPv6(null), "Null");
            Assert.IsFalse(HostAddress.IsExplicitIPv6(""), "Empty");
            Assert.IsFalse(HostAddress.IsExplicitIPv6("   "), "Whitespace");
        }

        #endregion

        #region IsBindOnlyAddress Tests

        [Test]
        public void IsBindOnlyAddress_WildcardAddresses_ReturnsTrue()
        {
            Assert.IsTrue(HostAddress.IsBindOnlyAddress("0.0.0.0"), "IPv4 wildcard");
            Assert.IsTrue(HostAddress.IsBindOnlyAddress("::"), "IPv6 wildcard short-form");
            Assert.IsTrue(HostAddress.IsBindOnlyAddress("0:0:0:0:0:0:0:0"), "IPv6 wildcard long-form");
        }

        [Test]
        public void IsBindOnlyAddress_NonWildcardAddresses_ReturnsFalse()
        {
            Assert.IsFalse(HostAddress.IsBindOnlyAddress("127.0.0.1"), "IPv4 loopback");
            Assert.IsFalse(HostAddress.IsBindOnlyAddress("::1"), "IPv6 loopback");
            Assert.IsFalse(HostAddress.IsBindOnlyAddress("localhost"), "localhost");
        }

        [Test]
        public void IsBindOnlyAddress_NullOrEmpty_ReturnsFalse()
        {
            Assert.IsFalse(HostAddress.IsBindOnlyAddress(null), "Null");
            Assert.IsFalse(HostAddress.IsBindOnlyAddress(""), "Empty");
            Assert.IsFalse(HostAddress.IsBindOnlyAddress("   "), "Whitespace");
        }

        [Test]
        public void IsBindOnlyAddress_TrimsWhitespace()
        {
            Assert.IsTrue(HostAddress.IsBindOnlyAddress(" 0.0.0.0 "), "IPv4 wildcard with spaces");
            Assert.IsTrue(HostAddress.IsBindOnlyAddress(" :: "), "IPv6 wildcard with spaces");
            Assert.IsTrue(HostAddress.IsBindOnlyAddress(" 0:0:0:0:0:0:0:0 "), "IPv6 long-form wildcard with spaces");
        }

        #endregion

        #region NormalizeForClient Tests

        [Test]
        public void NormalizeForClient_NullOrEmpty_ReturnsDefaultHost()
        {
            var result = HostAddress.NormalizeForClient(null);
            Assert.AreEqual(HostAddress.GetDefaultHost(), result, "Null should return default host");

            result = HostAddress.NormalizeForClient("");
            Assert.AreEqual(HostAddress.GetDefaultHost(), result, "Empty should return default host");

            result = HostAddress.NormalizeForClient("   ");
            Assert.AreEqual(HostAddress.GetDefaultHost(), result, "Whitespace should return default host");
        }

        [Test]
        public void NormalizeForClient_ExplicitIPv4_ReturnsUnchanged()
        {
            Assert.AreEqual("127.0.0.1", HostAddress.NormalizeForClient("127.0.0.1"));
            Assert.AreEqual("192.168.1.1", HostAddress.NormalizeForClient("192.168.1.1"));
        }

        [Test]
        public void NormalizeForClient_ExplicitIPv6_ReturnsUnchanged()
        {
            Assert.AreEqual("::1", HostAddress.NormalizeForClient("::1"));
            Assert.AreEqual("2001:db8::1", HostAddress.NormalizeForClient("2001:db8::1"));
            Assert.AreEqual("::ffff:127.0.0.1", HostAddress.NormalizeForClient("::ffff:127.0.0.1"));
        }

        [Test]
        public void NormalizeForClient_WildcardAddresses_ReturnsDefaultHost()
        {
            Assert.AreEqual(HostAddress.GetDefaultHost(), HostAddress.NormalizeForClient("0.0.0.0"));
            Assert.AreEqual(HostAddress.GetDefaultHost(), HostAddress.NormalizeForClient("::"));
            Assert.AreEqual(HostAddress.GetDefaultHost(), HostAddress.NormalizeForClient("0:0:0:0:0:0:0:0"), "IPv6 long-form wildcard");
        }

        [Test]
        public void NormalizeForClient_Localhost_ReturnsUnchanged()
        {
            Assert.AreEqual("localhost", HostAddress.NormalizeForClient("localhost"));
            Assert.AreEqual("LOCALHOST", HostAddress.NormalizeForClient("LOCALHOST"));
            Assert.AreEqual("LocalHost", HostAddress.NormalizeForClient("LocalHost"));
        }

        [Test]
        public void NormalizeForClient_OtherHostnames_ReturnsUnchanged()
        {
            Assert.AreEqual("example.com", HostAddress.NormalizeForClient("example.com"));
            Assert.AreEqual("192.168.1.100", HostAddress.NormalizeForClient("192.168.1.100"));
        }

        [Test]
        public void NormalizeForClient_TrimsWhitespace()
        {
            Assert.AreEqual("127.0.0.1", HostAddress.NormalizeForClient(" 127.0.0.1 "), "IPv4 with spaces");
            Assert.AreEqual("::1", HostAddress.NormalizeForClient(" ::1 "), "IPv6 with spaces");
            Assert.AreEqual("localhost", HostAddress.NormalizeForClient(" localhost "), "localhost with spaces");
            Assert.AreEqual("example.com", HostAddress.NormalizeForClient(" example.com "), "hostname with spaces");
            Assert.AreEqual(HostAddress.GetDefaultHost(), HostAddress.NormalizeForClient("   "), "Whitespace only returns default");
        }

        #endregion

        #region BuildConnectionList Tests

        [Test]
        public void BuildConnectionList_NullOrEmpty_ReturnsDefault()
        {
            var result = HostAddress.BuildConnectionList(null);
            Assert.IsNotEmpty(result, "Result should not be empty for null");
            Assert.AreEqual(HostAddress.GetDefaultHost(), result[0], "First host should be default host");

            result = HostAddress.BuildConnectionList("");
            Assert.IsNotEmpty(result, "Result should not be empty for empty string");
        }

        [Test]
        public void BuildConnectionList_ExplicitIPv4_SingleEntry()
        {
            var result = HostAddress.BuildConnectionList("127.0.0.1");
            Assert.AreEqual(1, result.Count, "IPv4 should return single entry");
            Assert.AreEqual("127.0.0.1", result[0]);
        }

        [Test]
        public void BuildConnectionList_ExplicitIPv6_SingleEntry()
        {
            var result = HostAddress.BuildConnectionList("::1");
            Assert.AreEqual(1, result.Count, "IPv6 loopback should return single entry");
            Assert.AreEqual("::1", result[0]);

            result = HostAddress.BuildConnectionList("::ffff:127.0.0.1");
            Assert.AreEqual(1, result.Count, "IPv4-mapped should return single entry");
            Assert.AreEqual("::ffff:127.0.0.1", result[0]);
        }

        [Test]
        public void BuildConnectionList_Localhost_IPv4OnlyByDefault()
        {
            var result = HostAddress.BuildConnectionList("localhost");
            Assert.AreEqual(1, result.Count, "localhost should return single entry by default (IPv6 fallback disabled)");
            Assert.AreEqual("127.0.0.1", result[0], "IPv4 should be returned");
        }

        [Test]
        public void BuildConnectionList_Wildcard_IPv4OnlyByDefault()
        {
            var result = HostAddress.BuildConnectionList("0.0.0.0");
            Assert.AreEqual(1, result.Count, "IPv4 wildcard should return single entry by default (IPv6 fallback disabled)");
            Assert.AreEqual("127.0.0.1", result[0]);

            result = HostAddress.BuildConnectionList("::");
            Assert.AreEqual(1, result.Count, "IPv6 wildcard short-form should return single entry by default (IPv6 fallback disabled)");
            Assert.AreEqual("127.0.0.1", result[0]);

            result = HostAddress.BuildConnectionList("0:0:0:0:0:0:0:0");
            Assert.AreEqual(1, result.Count, "IPv6 wildcard long-form should return single entry by default (IPv6 fallback disabled)");
            Assert.AreEqual("127.0.0.1", result[0]);
        }

        [Test]
        public void BuildConnectionList_OtherHostnames_SingleEntry()
        {
            var result = HostAddress.BuildConnectionList("example.com");
            Assert.AreEqual(1, result.Count, "Other hostname should return single entry");
            Assert.AreEqual("example.com", result[0]);
        }

        [Test]
        public void BuildConnectionList_IPv6FallbackEnabled_IncludesBoth()
        {
            var result = HostAddress.BuildConnectionList("localhost", enableIPv6Fallback: true);
            Assert.AreEqual(2, result.Count, "With IPv6 fallback enabled, should return two entries");
            Assert.AreEqual("127.0.0.1", result[0], "IPv4 should be first");
            Assert.AreEqual("::1", result[1], "IPv6 should be second");

            result = HostAddress.BuildConnectionList("0.0.0.0", enableIPv6Fallback: true);
            Assert.AreEqual(2, result.Count, "Wildcard with IPv6 fallback enabled should return two entries");
            Assert.AreEqual("127.0.0.1", result[0], "IPv4 should be first");
            Assert.AreEqual("::1", result[1], "IPv6 should be second");
        }

        [Test]
        public void BuildConnectionList_TrimsWhitespace()
        {
            var result = HostAddress.BuildConnectionList(" localhost ");
            Assert.AreEqual(1, result.Count, "localhost with spaces should return single entry");
            Assert.AreEqual("127.0.0.1", result[0], "IPv4 should be returned for localhost");

            result = HostAddress.BuildConnectionList(" 127.0.0.1 ");
            Assert.AreEqual(1, result.Count, "IPv4 with spaces should return single entry");
            Assert.AreEqual("127.0.0.1", result[0]);

            result = HostAddress.BuildConnectionList(" ::1 ");
            Assert.AreEqual(1, result.Count, "IPv6 with spaces should return single entry");
            Assert.AreEqual("::1", result[0]);
        }

        #endregion

        #region GetDefaultHost Tests

        [Test]
        public void GetDefaultHost_ReturnsValidHost()
        {
            var result = HostAddress.GetDefaultHost();
            Assert.IsNotNull(result, "Default host should not be null");
            Assert.IsNotEmpty(result, "Default host should not be empty");
            // On Windows it should be "127.0.0.1", on other platforms "localhost"
            Assert.IsTrue(result == "127.0.0.1" || result == "localhost",
                $"Default host should be either '127.0.0.1' or 'localhost', got '{result}'");
        }

        #endregion
    }
}
