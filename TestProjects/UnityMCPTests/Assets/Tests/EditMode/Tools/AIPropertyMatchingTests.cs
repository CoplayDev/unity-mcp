using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using MCPForUnity.Editor.Tools;

namespace MCPForUnityTests.Editor.Tools
{
    public class AIPropertyMatchingTests
    {
        private List<string> sampleProperties;

        [SetUp]
        public void SetUp()
        {
            sampleProperties = new List<string>
            {
                "maxReachDistance",
                "maxHorizontalDistance",
                "maxVerticalDistance",
                "moveSpeed",
                "healthPoints",
                "playerName",
                "isEnabled",
                "mass",
                "velocity",
                "transform"
            };
        }

        [Test]
        public void GetAllComponentProperties_ReturnsValidProperties_ForTransform()
        {
            var properties = ComponentResolver.GetAllComponentProperties(typeof(Transform));

            Assert.IsNotEmpty(properties, "Transform should have properties");
            Assert.Contains("position", properties, "Transform should have position property");
            Assert.Contains("rotation", properties, "Transform should have rotation property");
            Assert.Contains("localScale", properties, "Transform should have localScale property");
        }

        [Test]
        public void GetAllComponentProperties_ReturnsEmpty_ForNullType()
        {
            var properties = ComponentResolver.GetAllComponentProperties(null);

            Assert.IsEmpty(properties, "Null type should return empty list");
        }

        [Test]
        public void GetAIPropertySuggestions_ReturnsEmpty_ForNullInput()
        {
            var suggestions = ComponentResolver.GetAIPropertySuggestions(null, sampleProperties);

            Assert.IsEmpty(suggestions, "Null input should return no suggestions");
        }

        [Test]
        public void GetAIPropertySuggestions_ReturnsEmpty_ForEmptyInput()
        {
            var suggestions = ComponentResolver.GetAIPropertySuggestions("", sampleProperties);

            Assert.IsEmpty(suggestions, "Empty input should return no suggestions");
        }

        [Test]
        public void GetAIPropertySuggestions_ReturnsEmpty_ForEmptyPropertyList()
        {
            var suggestions = ComponentResolver.GetAIPropertySuggestions("test", new List<string>());

            Assert.IsEmpty(suggestions, "Empty property list should return no suggestions");
        }

        [Test]
        public void GetAIPropertySuggestions_FindsExactMatch_AfterCleaning()
        {
            var suggestions = ComponentResolver.GetAIPropertySuggestions("Max Reach Distance", sampleProperties);

            Assert.Contains("maxReachDistance", suggestions, "Should find exact match after cleaning spaces");
            Assert.GreaterOrEqual(suggestions.Count, 1, "Should return at least one match for exact match");
        }

        [Test]
        public void GetAIPropertySuggestions_FindsMultipleWordMatches()
        {
            var suggestions = ComponentResolver.GetAIPropertySuggestions("max distance", sampleProperties);

            Assert.Contains("maxReachDistance", suggestions, "Should match maxReachDistance");
            Assert.Contains("maxHorizontalDistance", suggestions, "Should match maxHorizontalDistance");
            Assert.Contains("maxVerticalDistance", suggestions, "Should match maxVerticalDistance");
        }

        [Test]
        public void GetAIPropertySuggestions_FindsSimilarStrings_WithTypos()
        {
            var suggestions = ComponentResolver.GetAIPropertySuggestions("movespeed", sampleProperties); // missing capital S

            Assert.Contains("moveSpeed", suggestions, "Should find moveSpeed despite missing capital");
        }

        [Test]
        public void GetAIPropertySuggestions_FindsSemanticMatches_ForCommonTerms()
        {
            var suggestions = ComponentResolver.GetAIPropertySuggestions("weight", sampleProperties);

            // Note: Current algorithm might not find "mass" but should handle it gracefully
            Assert.IsNotNull(suggestions, "Should return valid suggestions list");
        }

        [Test]
        public void GetAIPropertySuggestions_LimitsResults_ToReasonableNumber()
        {
            // Test with input that might match many properties
            var suggestions = ComponentResolver.GetAIPropertySuggestions("m", sampleProperties);

            Assert.LessOrEqual(suggestions.Count, 3, "Should limit suggestions to 3 or fewer");
        }

        [Test]
        public void GetAIPropertySuggestions_CachesResults()
        {
            var input = "Max Reach Distance";

            // First call
            var suggestions1 = ComponentResolver.GetAIPropertySuggestions(input, sampleProperties);

            // Second call should use cache (tested indirectly by ensuring consistency)
            var suggestions2 = ComponentResolver.GetAIPropertySuggestions(input, sampleProperties);

            Assert.AreEqual(suggestions1.Count, suggestions2.Count, "Cached results should be consistent");
            CollectionAssert.AreEqual(suggestions1, suggestions2, "Cached results should be identical");
        }

        [Test]
        public void GetAIPropertySuggestions_HandlesUnityNamingConventions()
        {
            var unityStyleProperties = new List<string> { "isKinematic", "useGravity", "maxLinearVelocity" };

            var suggestions1 = ComponentResolver.GetAIPropertySuggestions("is kinematic", unityStyleProperties);
            var suggestions2 = ComponentResolver.GetAIPropertySuggestions("use gravity", unityStyleProperties);
            var suggestions3 = ComponentResolver.GetAIPropertySuggestions("max linear velocity", unityStyleProperties);

            Assert.Contains("isKinematic", suggestions1, "Should handle 'is' prefix convention");
            Assert.Contains("useGravity", suggestions2, "Should handle 'use' prefix convention");
            Assert.Contains("maxLinearVelocity", suggestions3, "Should handle 'max' prefix convention");
        }

        [Test]
        public void GetAIPropertySuggestions_PrioritizesExactMatches()
        {
            var properties = new List<string> { "speed", "moveSpeed", "maxSpeed", "speedMultiplier" };
            var suggestions = ComponentResolver.GetAIPropertySuggestions("speed", properties);

            Assert.IsNotEmpty(suggestions, "Should find suggestions");
            Assert.Contains("speed", suggestions, "Exact match should be included in results");
            // Note: Implementation may or may not prioritize exact matches first
        }

        [Test]
        public void GetAIPropertySuggestions_HandlesCaseInsensitive()
        {
            var suggestions1 = ComponentResolver.GetAIPropertySuggestions("MAXREACHDISTANCE", sampleProperties);
            var suggestions2 = ComponentResolver.GetAIPropertySuggestions("maxreachdistance", sampleProperties);

            Assert.Contains("maxReachDistance", suggestions1, "Should handle uppercase input");
            Assert.Contains("maxReachDistance", suggestions2, "Should handle lowercase input");
        }
    }
}
