using System;
using System.Reflection;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// Tests for ManageScript delimiter-checking and token-finding logic,
    /// specifically covering C# string variants that the old lexer missed:
    /// verbatim strings, interpolated strings, raw string literals.
    /// </summary>
    public class ManageScriptDelimiterTests
    {
        // ── CheckBalancedDelimiters ──────────────────────────────────────

        [Test]
        public void CheckBalancedDelimiters_VerbatimString_WithBackslashes()
        {
            // @"C:\Users\file" — backslashes are NOT escape chars in verbatim strings
            string code = "class C { string s = @\"C:\\Users\\file\"; }";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Verbatim string with backslashes should not break delimiter balance");
        }

        [Test]
        public void CheckBalancedDelimiters_VerbatimString_DoubledQuotes()
        {
            // @"He said ""hello""" — doubled quotes are the escape in verbatim strings
            string code = "class C { string s = @\"He said \"\"hello\"\"\"; }";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Verbatim string with doubled quotes should not break delimiter balance");
        }

        [Test]
        public void CheckBalancedDelimiters_InterpolatedString_WithBraces()
        {
            // $"Value: {x}" — the { } are interpolation holes, not real braces
            string code = "class C { void M() { int x = 1; string s = $\"Value: {x}\"; } }";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Interpolated string braces should not be counted as delimiters");
        }

        [Test]
        public void CheckBalancedDelimiters_InterpolatedVerbatim_Combined()
        {
            // $@"Path: {dir}\file" — interpolated + verbatim combined
            string code = "class C { void M() { string dir = \"d\"; string s = $@\"Path: {dir}\\file\"; } }";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Interpolated verbatim string should not break delimiter balance");
        }

        [Test]
        public void CheckBalancedDelimiters_NestedInterpolation()
        {
            // $"Outer {$"Inner {x}"}" — nested interpolated strings
            string code = "class C { void M() { int x = 1; string s = $\"Outer {$\"Inner {x}\"}\"; } }";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Nested interpolated strings should not break delimiter balance");
        }

        [Test]
        public void CheckBalancedDelimiters_RawStringLiteral()
        {
            // C# 11 raw string literal: """{ }"""
            string code = "class C { string s = \"\"\"\n{ }\n\"\"\"; }";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Raw string literal braces should not be counted as delimiters");
        }

        [Test]
        public void CheckBalancedDelimiters_MultilineVerbatimString()
        {
            // Verbatim string spanning multiple lines with braces
            string code = "class C { string s = @\"line1\n{ }\"; }";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Multiline verbatim string with braces should not break balance");
        }

        [Test]
        public void CheckBalancedDelimiters_InterpolatedEscapedBraces()
        {
            // $"literal {{braces}}" — escaped braces in interpolated string
            string code = "class C { string s = $\"literal {{braces}}\"; }";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Escaped braces in interpolated strings should not break balance");
        }

        [Test]
        public void CheckBalancedDelimiters_InterpolatedRawString()
        {
            // $"""...{expr}...""" — interpolated raw string literal (C# 11)
            string code = "class C { void M() { int x = 1; string s = $\"\"\"\n    Hello {x}\n    \"\"\"; } }";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Interpolated raw string should not break delimiter balance");
        }

        [Test]
        public void CheckBalancedDelimiters_MultiDollarRawString()
        {
            // $$"""...{{expr}}...""" — multi-dollar interpolated raw string
            string code = "class C { void M() { int x = 1; string s = $$\"\"\"\n    {literal} {{x}}\n    \"\"\"; } }";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Multi-dollar raw string should not break delimiter balance");
        }

        [Test]
        public void CheckBalancedDelimiters_BracesInComments_Ignored()
        {
            string code = "class C {\n// {\n/* { */\nvoid M() { }\n}";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Braces in comments should be ignored");
        }

        [Test]
        public void CheckBalancedDelimiters_BracesInRegularStrings_Ignored()
        {
            string code = "class C { string s = \"{ }\"; }";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Braces in regular strings should be ignored");
        }

        [Test]
        public void CheckBalancedDelimiters_ActuallyUnbalanced_ReturnsFalse()
        {
            string code = "class C { void M() { }";
            Assert.IsFalse(CallCheckBalancedDelimiters(code, out _, out _),
                "Actually unbalanced code should return false");
        }

        [Test]
        public void CheckBalancedDelimiters_ExtraClosingBrace_ReturnsFalse()
        {
            string code = "class C { } }";
            Assert.IsFalse(CallCheckBalancedDelimiters(code, out _, out _),
                "Extra closing brace should return false");
        }

        [Test]
        public void CheckBalancedDelimiters_RealWorldUnityScript()
        {
            string code = @"using UnityEngine;

public class PlayerHUD : MonoBehaviour
{
    private int score;
    private string playerName;

    void Start()
    {
        score = 0;
        playerName = ""Player"";
    }

    void OnGUI()
    {
        string label = $""Score: {score}"";
        string path = @""C:\Games\SaveData"";
        string msg = $@""Player {playerName} at path {path}"";
        Debug.Log($""HUD initialized for {playerName} with score {score}"");
        Debug.Log(""Literal {{braces}}"");
    }
}";
            Assert.IsTrue(CallCheckBalancedDelimiters(code, out _, out _),
                "Real-world Unity script with interpolated/verbatim strings should pass");
        }

        // ── IndexOfClassToken ────────────────────────────────────────────

        [Test]
        public void IndexOfClassToken_FindsClass_NormalCode()
        {
            string code = "public class Foo { }";
            int idx = CallIndexOfClassToken(code, "Foo");
            Assert.GreaterOrEqual(idx, 0, "Should find class Foo in normal code");
        }

        [Test]
        public void IndexOfClassToken_SkipsClassInComment()
        {
            string code = "// class Foo\npublic class Real { }";
            int idx = CallIndexOfClassToken(code, "Foo");
            Assert.AreEqual(-1, idx, "Should not find 'class Foo' inside a comment");
        }

        [Test]
        public void IndexOfClassToken_SkipsClassInString()
        {
            string code = "class Real { string s = \"class Foo { }\"; }";
            int idx = CallIndexOfClassToken(code, "Foo");
            Assert.AreEqual(-1, idx, "Should not find 'class Foo' inside a string literal");
        }

        [Test]
        public void IndexOfClassToken_FindsSecondClass_WhenFirstInComment()
        {
            string code = "// class Fake\npublic class Real { }";
            int idx = CallIndexOfClassToken(code, "Real");
            Assert.GreaterOrEqual(idx, 0, "Should find class Real even when a commented class precedes it");
        }

        // ── Reflection helpers ───────────────────────────────────────────

        private static bool CallCheckBalancedDelimiters(string text, out int line, out char expected)
        {
            line = 0;
            expected = '\0';

            var method = typeof(ManageScript).GetMethod("CheckBalancedDelimiters",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "CheckBalancedDelimiters method should exist");

            var parameters = new object[] { text, 0, '\0' };
            var result = (bool)method.Invoke(null, parameters);
            line = (int)parameters[1];
            expected = (char)parameters[2];
            return result;
        }

        private static int CallIndexOfClassToken(string source, string className)
        {
            var method = typeof(ManageScript).GetMethod("IndexOfClassToken",
                BindingFlags.NonPublic | BindingFlags.Static);
            Assert.IsNotNull(method, "IndexOfClassToken method should exist");

            return (int)method.Invoke(null, new object[] { source, className });
        }
    }
}
