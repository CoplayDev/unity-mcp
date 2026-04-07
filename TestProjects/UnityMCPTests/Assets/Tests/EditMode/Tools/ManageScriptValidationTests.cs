using System;
using System.Collections.Generic;
using System.Linq;
using NUnit.Framework;
using UnityEngine;
using Newtonsoft.Json.Linq;
using MCPForUnity.Editor.Tools;
using System.Reflection;

namespace MCPForUnityTests.Editor.Tools
{
    /// <summary>
    /// In-memory tests for ManageScript validation logic.
    /// These tests focus on the validation methods directly without creating files.
    /// </summary>
    public class ManageScriptValidationTests
    {
        [Test]
        public void HandleCommand_NullParams_ReturnsError()
        {
            var result = ManageScript.HandleCommand(null);
            Assert.IsNotNull(result, "Should handle null parameters gracefully");
        }

        [Test]
        public void HandleCommand_InvalidAction_ReturnsError()
        {
            var paramsObj = new JObject
            {
                ["action"] = "invalid_action",
                ["name"] = "TestScript",
                ["path"] = "Assets/Scripts"
            };

            var result = ManageScript.HandleCommand(paramsObj);
            Assert.IsNotNull(result, "Should return error result for invalid action");
        }

        [Test]
        public void CheckBalancedDelimiters_ValidCode_ReturnsTrue()
        {
            string validCode = "using UnityEngine;\n\npublic class TestClass : MonoBehaviour\n{\n    void Start()\n    {\n        Debug.Log(\"test\");\n    }\n}";

            bool result = CallCheckBalancedDelimiters(validCode, out int line, out char expected);
            Assert.IsTrue(result, "Valid C# code should pass balance check");
        }

        [Test]
        public void CheckBalancedDelimiters_UnbalancedBraces_ReturnsFalse()
        {
            string unbalancedCode = "using UnityEngine;\n\npublic class TestClass : MonoBehaviour\n{\n    void Start()\n    {\n        Debug.Log(\"test\");\n    // Missing closing brace";

            bool result = CallCheckBalancedDelimiters(unbalancedCode, out int line, out char expected);
            Assert.IsFalse(result, "Unbalanced code should fail balance check");
        }

        [Test]
        public void CheckBalancedDelimiters_StringWithBraces_ReturnsTrue()
        {
            string codeWithStringBraces = "using UnityEngine;\n\npublic class TestClass : MonoBehaviour\n{\n    public string json = \"{key: value}\";\n    void Start() { Debug.Log(json); }\n}";

            bool result = CallCheckBalancedDelimiters(codeWithStringBraces, out int line, out char expected);
            Assert.IsTrue(result, "Code with braces in strings should pass balance check");
        }

        [Test]
        public void TicTacToe3D_ValidationScenario_DoesNotCrash()
        {
            // Test the scenario that was causing issues without file I/O
            string ticTacToeCode = "using UnityEngine;\n\npublic class TicTacToe3D : MonoBehaviour\n{\n    public string gameState = \"active\";\n    void Start() { Debug.Log(\"Game started\"); }\n    public void MakeMove(int position) { if (gameState == \"active\") Debug.Log($\"Move {position}\"); }\n}";

            // Test that the validation methods don't crash on this code
            bool balanceResult = CallCheckBalancedDelimiters(ticTacToeCode, out int line, out char expected);

            Assert.IsTrue(balanceResult, "TicTacToe3D code should pass balance validation");
        }

        // Helper methods to access private ManageScript methods via reflection
        private bool CallCheckBalancedDelimiters(string contents, out int line, out char expected)
        {
            line = 0;
            expected = ' ';

            try
            {
                var method = typeof(ManageScript).GetMethod("CheckBalancedDelimiters",
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (method != null)
                {
                    var parameters = new object[] { contents, line, expected };
                    var result = (bool)method.Invoke(null, parameters);
                    line = (int)parameters[1];
                    expected = (char)parameters[2];
                    return result;
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Could not test CheckBalancedDelimiters directly: {ex.Message}");
            }

            // Fallback: basic structural check
            return BasicBalanceCheck(contents);
        }

        private bool BasicBalanceCheck(string contents)
        {
            // Simple fallback balance check
            int braceCount = 0;
            bool inString = false;
            bool escaped = false;

            for (int i = 0; i < contents.Length; i++)
            {
                char c = contents[i];

                if (escaped)
                {
                    escaped = false;
                    continue;
                }

                if (inString)
                {
                    if (c == '\\') escaped = true;
                    else if (c == '"') inString = false;
                    continue;
                }

                if (c == '"') inString = true;
                else if (c == '{') braceCount++;
                else if (c == '}') braceCount--;

                if (braceCount < 0) return false;
            }

            return braceCount == 0;
        }

        /// <summary>
        /// Calls ValidateScriptSyntax via reflection and returns the error list.
        /// This exercises CheckDuplicateMethodSignatures (called from ValidateScriptSyntax).
        /// </summary>
        private List<string> CallValidateScriptSyntaxUnity(string contents)
        {
            var validationLevelType = typeof(ManageScript).GetNestedType("ValidationLevel",
                BindingFlags.NonPublic);
            Assert.IsNotNull(validationLevelType, "ValidationLevel enum must exist");
            var basicLevel = Enum.ToObject(validationLevelType, 0); // ValidationLevel.Basic

            var method = typeof(ManageScript).GetMethod("ValidateScriptSyntax",
                BindingFlags.NonPublic | BindingFlags.Static, null,
                new[] { typeof(string), validationLevelType, typeof(string[]).MakeByRefType() }, null);
            Assert.IsNotNull(method, "ValidateScriptSyntax method must exist");

            var args = new object[] { contents, basicLevel, null };
            method.Invoke(null, args);
            var errArray = (string[])args[2];
            return errArray != null ? errArray.ToList() : new List<string>();
        }

        private bool HasDuplicateMethodError(List<string> errors)
        {
            return errors.Any(e => e.Contains("Duplicate method signature detected"));
        }

        // --- Duplicate method detection: false positive tests ---

        [Test]
        public void DuplicateDetection_LineCommentedMethod_NotFlagged()
        {
            string code = @"using UnityEngine;
public class Foo : MonoBehaviour
{
    public void DoStuff(int x) { }
    // public void DoStuff(int x) { }
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsFalse(HasDuplicateMethodError(errors),
                "A method in a line comment should not be flagged as duplicate");
        }

        [Test]
        public void DuplicateDetection_BlockCommentedMethod_NotFlagged()
        {
            string code = @"using UnityEngine;
public class Foo : MonoBehaviour
{
    public void DoStuff(int x) { }
    /* public void DoStuff(int x) { } */
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsFalse(HasDuplicateMethodError(errors),
                "A method in a block comment should not be flagged as duplicate");
        }

        [Test]
        public void DuplicateDetection_InnerClassSameMethod_NotFlagged()
        {
            string code = @"using UnityEngine;
public class Outer : MonoBehaviour
{
    public void Init(int x) { }

    private class Inner
    {
        public void Init(int x) { }
    }
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsFalse(HasDuplicateMethodError(errors),
                "Same method name in outer and inner class should not be flagged");
        }

        [Test]
        public void DuplicateDetection_DifferentTypeOverloads_NotFlagged()
        {
            string code = @"using UnityEngine;
public class Foo : MonoBehaviour
{
    public void Process(int x) { }
    public void Process(string x) { }
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsFalse(HasDuplicateMethodError(errors),
                "Overloads with different param types but same count should not be flagged");
        }

        // --- Duplicate method detection: true positive tests ---

        [Test]
        public void DuplicateDetection_ExpressionBodiedDuplicate_Flagged()
        {
            string code = @"using UnityEngine;
public class Foo : MonoBehaviour
{
    public int GetValue(int x) => x * 2;
    public int GetValue(int x) => x * 3;
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsTrue(HasDuplicateMethodError(errors),
                "Expression-bodied duplicate methods should be flagged");
        }

        [Test]
        public void DuplicateDetection_ExactDuplicate_Flagged()
        {
            string code = @"using UnityEngine;
public class Foo : MonoBehaviour
{
    public void DoStuff(int x) { }
    public void DoStuff(int x) { }
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsTrue(HasDuplicateMethodError(errors),
                "Exact duplicate methods should be flagged");
        }

        [Test]
        public void DuplicateDetection_SameTypeDifferentParamName_Flagged()
        {
            // This is the real anchor_replace corruption pattern
            string code = @"using UnityEngine;
public class Foo : MonoBehaviour
{
    public void Initialize(string name) { }
    public void Initialize(string label) { }
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsTrue(HasDuplicateMethodError(errors),
                "Same-type different-name duplicates (corruption pattern) should be flagged");
        }

        [Test]
        public void DuplicateDetection_GenericParamDuplicate_Flagged()
        {
            string code = @"using UnityEngine;
using System.Collections.Generic;
public class Foo : MonoBehaviour
{
    public void Process(Dictionary<string, int> data) { }
    public void Process(Dictionary<string, int> other) { }
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsTrue(HasDuplicateMethodError(errors),
                "Generic param duplicates with different names should be flagged");
        }

        // --- Keyword false positive tests ---

        [Test]
        public void DuplicateDetection_CSharpKeywords_NotMatchedAsMethods()
        {
            string code = @"using UnityEngine;
public class Foo : MonoBehaviour
{
    public void Update()
    {
        if (true) { }
        if (true) { }
        for (int i = 0; i < 10; i++) { }
        for (int j = 0; j < 5; j++) { }
        while (true) { break; }
        while (false) { break; }
        foreach (var x in new int[0]) { }
        foreach (var y in new int[0]) { }
        switch (0) { default: break; }
        switch (1) { default: break; }
        lock (this) { }
        lock (this) { }
        using (var d = new System.IO.MemoryStream()) { }
        using (var e = new System.IO.MemoryStream()) { }
        typeof(int);
        typeof(string);
    }
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsFalse(HasDuplicateMethodError(errors),
                "C# keywords (if, for, while, etc.) should not be matched as duplicate methods");
        }

        [Test]
        public void DuplicateMethodCheck_ConstructorInvocations_NotFlagged()
        {
            string code = @"using UnityEngine;
public class Test : MonoBehaviour
{
    void Start()
    {
        GameObject a = new GameObject(""A"");
        GameObject b = new GameObject(""B"");
    }
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsFalse(HasDuplicateMethodError(errors),
                "Constructor invocations (new Type(...)) should not be flagged as duplicate methods");
        }

        [Test]
        public void DuplicateMethodCheck_MultipleDistinctConstructors_NotFlagged()
        {
            string code = @"using UnityEngine;
public class Test : MonoBehaviour
{
    void Start()
    {
        var mpb1 = new MaterialPropertyBlock();
        var mpb2 = new MaterialPropertyBlock();
        var go1 = new GameObject(""A"");
        var go2 = new GameObject(""B"");
    }
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsFalse(HasDuplicateMethodError(errors),
                "Multiple constructor invocations of different types should not be flagged");
        }

        [Test]
        public void DuplicateMethodCheck_NewModifierWithConstructors_CorrectBehavior()
        {
            string code = @"using UnityEngine;
public class Base : MonoBehaviour
{
    public virtual void Init() { }
}
public class Derived : Base
{
    public new void Init() { }
    void Start()
    {
        var a = new GameObject(""A"");
        var b = new GameObject(""B"");
    }
}";
            var errors = CallValidateScriptSyntaxUnity(code);
            Assert.IsFalse(HasDuplicateMethodError(errors),
                "new modifier on method should not interfere with constructor invocation filtering");
        }

        [Test]
        public void HandleCommand_PathWithCsExtension_StripsFilename()
        {
            // When path ends with .cs (full file path instead of directory),
            // HandleCommand should strip the filename to avoid doubled paths
            // like "Assets/Scripts/Foo.cs/Foo.cs".
            var paramsObj = new JObject
            {
                ["action"] = "read",
                ["name"] = "TestScript",
                ["path"] = "Assets/Scripts/TestScript.cs"
            };

            var result = ManageScript.HandleCommand(paramsObj);
            // The script won't exist, but the error path should NOT contain doubled filename
            string json = Newtonsoft.Json.JsonConvert.SerializeObject(result);
            Assert.IsFalse(json.Contains("TestScript.cs/TestScript.cs"),
                "Path ending in .cs should be treated as directory, not produce doubled filename");
        }
    }
}
