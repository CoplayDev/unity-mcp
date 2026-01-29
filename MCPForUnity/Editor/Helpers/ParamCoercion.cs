using System;
using System.Globalization;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Utility class for coercing JSON parameter values to strongly-typed values.
    /// Handles various input formats (strings, numbers, booleans) gracefully.
    /// </summary>
    public static class ParamCoercion
    {
        /// <summary>
        /// Coerces a JToken to an integer value, handling strings and floats.
        /// </summary>
        /// <param name="token">The JSON token to coerce</param>
        /// <param name="defaultValue">Default value if coercion fails</param>
        /// <returns>The coerced integer value or default</returns>
        public static int CoerceInt(JToken token, int defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null)
                return defaultValue;

            try
            {
                if (token.Type == JTokenType.Integer)
                    return token.Value<int>();

                var s = token.ToString().Trim();
                if (s.Length == 0)
                    return defaultValue;

                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    return i;

                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return (int)d;
            }
            catch
            {
                // Swallow and return default
            }

            return defaultValue;
        }

        /// <summary>
        /// Coerces a JToken to a nullable integer value.
        /// Returns null if token is null, empty, or cannot be parsed.
        /// </summary>
        /// <param name="token">The JSON token to coerce</param>
        /// <returns>The coerced integer value or null</returns>
        public static int? CoerceIntNullable(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                if (token.Type == JTokenType.Integer)
                    return token.Value<int>();

                var s = token.ToString().Trim();
                if (s.Length == 0)
                    return null;

                if (int.TryParse(s, NumberStyles.Integer, CultureInfo.InvariantCulture, out var i))
                    return i;

                if (double.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var d))
                    return (int)d;
            }
            catch
            {
                // Swallow and return null
            }

            return null;
        }

        /// <summary>
        /// Coerces a JToken to a boolean value, handling strings like "true", "1", etc.
        /// </summary>
        /// <param name="token">The JSON token to coerce</param>
        /// <param name="defaultValue">Default value if coercion fails</param>
        /// <returns>The coerced boolean value or default</returns>
        public static bool CoerceBool(JToken token, bool defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null)
                return defaultValue;

            try
            {
                if (token.Type == JTokenType.Boolean)
                    return token.Value<bool>();

                var s = token.ToString().Trim().ToLowerInvariant();
                if (s.Length == 0)
                    return defaultValue;

                if (bool.TryParse(s, out var b))
                    return b;

                if (s == "1" || s == "yes" || s == "on")
                    return true;

                if (s == "0" || s == "no" || s == "off")
                    return false;
            }
            catch
            {
                // Swallow and return default
            }

            return defaultValue;
        }

        /// <summary>
        /// Coerces a JToken to a nullable boolean value.
        /// Returns null if token is null, empty, or cannot be parsed.
        /// </summary>
        /// <param name="token">The JSON token to coerce</param>
        /// <returns>The coerced boolean value or null</returns>
        public static bool? CoerceBoolNullable(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                if (token.Type == JTokenType.Boolean)
                    return token.Value<bool>();

                var s = token.ToString().Trim().ToLowerInvariant();
                if (s.Length == 0)
                    return null;

                if (bool.TryParse(s, out var b))
                    return b;

                if (s == "1" || s == "yes" || s == "on")
                    return true;

                if (s == "0" || s == "no" || s == "off")
                    return false;
            }
            catch
            {
                // Swallow and return null
            }

            return null;
        }

        /// <summary>
        /// Coerces a JToken to a float value, handling strings and integers.
        /// </summary>
        /// <param name="token">The JSON token to coerce</param>
        /// <param name="defaultValue">Default value if coercion fails</param>
        /// <returns>The coerced float value or default</returns>
        public static float CoerceFloat(JToken token, float defaultValue)
        {
            if (token == null || token.Type == JTokenType.Null)
                return defaultValue;

            try
            {
                if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                    return token.Value<float>();

                var s = token.ToString().Trim();
                if (s.Length == 0)
                    return defaultValue;

                if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    return f;
            }
            catch
            {
                // Swallow and return default
            }

            return defaultValue;
        }

        /// <summary>
        /// Coerces a JToken to a nullable float value.
        /// Returns null if token is null, empty, or cannot be parsed.
        /// </summary>
        /// <param name="token">The JSON token to coerce</param>
        /// <returns>The coerced float value or null</returns>
        public static float? CoerceFloatNullable(JToken token)
        {
            if (token == null || token.Type == JTokenType.Null)
                return null;

            try
            {
                if (token.Type == JTokenType.Float || token.Type == JTokenType.Integer)
                    return token.Value<float>();

                var s = token.ToString().Trim();
                if (s.Length == 0)
                    return null;

                if (float.TryParse(s, NumberStyles.Float, CultureInfo.InvariantCulture, out var f))
                    return f;
            }
            catch
            {
                // Swallow and return null
            }

            return null;
        }

        /// <summary>
        /// Coerces a JToken to a string value, with null handling.
        /// </summary>
        /// <param name="token">The JSON token to coerce</param>
        /// <param name="defaultValue">Default value if null or empty</param>
        /// <returns>The string value or default</returns>
        public static string CoerceString(JToken token, string defaultValue = null)
        {
            if (token == null || token.Type == JTokenType.Null)
                return defaultValue;

            var s = token.ToString();
            return string.IsNullOrEmpty(s) ? defaultValue : s;
        }

        /// <summary>
        /// Coerces a JToken to an enum value, handling strings.
        /// </summary>
        /// <typeparam name="T">The enum type</typeparam>
        /// <param name="token">The JSON token to coerce</param>
        /// <param name="defaultValue">Default value if coercion fails</param>
        /// <returns>The coerced enum value or default</returns>
        public static T CoerceEnum<T>(JToken token, T defaultValue) where T : struct, Enum
        {
            if (token == null || token.Type == JTokenType.Null)
                return defaultValue;

            try
            {
                var s = token.ToString().Trim();
                if (s.Length == 0)
                    return defaultValue;

                if (Enum.TryParse<T>(s, ignoreCase: true, out var result))
                    return result;
            }
            catch
            {
                // Swallow and return default
            }

            return defaultValue;
        }

        /// <summary>
        /// Checks if a JToken represents a numeric value (integer or float).
        /// Useful for validating JSON values before parsing.
        /// </summary>
        /// <param name="token">The JSON token to check</param>
        /// <returns>True if the token is an integer or float, false otherwise</returns>
        public static bool IsNumericToken(JToken token)
        {
            return token != null && (token.Type == JTokenType.Integer || token.Type == JTokenType.Float);
        }
        
        /// <summary>
        /// Validates that an optional field in a JObject is numeric if present.
        /// Used for dry-run validation of complex type formats.
        /// </summary>
        /// <param name="obj">The JSON object containing the field</param>
        /// <param name="fieldName">The name of the field to validate</param>
        /// <param name="error">Output error message if validation fails</param>
        /// <returns>True if the field is absent, null, or numeric; false if present but non-numeric</returns>
        public static bool ValidateNumericField(JObject obj, string fieldName, out string error)
        {
            error = null;
            var token = obj[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return true; // Field not present, valid (will use default)
            }
            if (!IsNumericToken(token))
            {
                error = $"must be a number, got {token.Type}";
                return false;
            }
            return true;
        }
        
        /// <summary>
        /// Validates that an optional field in a JObject is an integer if present.
        /// Used for dry-run validation of complex type formats.
        /// </summary>
        /// <param name="obj">The JSON object containing the field</param>
        /// <param name="fieldName">The name of the field to validate</param>
        /// <param name="error">Output error message if validation fails</param>
        /// <returns>True if the field is absent, null, or integer; false if present but non-integer</returns>
        public static bool ValidateIntegerField(JObject obj, string fieldName, out string error)
        {
            error = null;
            var token = obj[fieldName];
            if (token == null || token.Type == JTokenType.Null)
            {
                return true; // Field not present, valid
            }
            if (token.Type != JTokenType.Integer)
            {
                error = $"must be an integer, got {token.Type}";
                return false;
            }
            return true;
        }

        /// <summary>
        /// Normalizes a property name by removing separators and converting to camelCase.
        /// Handles common naming variations from LLMs and humans.
        /// Examples:
        ///   "Use Gravity" → "useGravity"
        ///   "is_kinematic" → "isKinematic"
        ///   "max-angular-velocity" → "maxAngularVelocity"
        ///   "Angular Drag" → "angularDrag"
        /// </summary>
        /// <param name="input">The property name to normalize</param>
        /// <returns>The normalized camelCase property name</returns>
        public static string NormalizePropertyName(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // Split on common separators: space, underscore, dash
            var parts = input.Split(new[] { ' ', '_', '-' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length == 0)
                return input;

            // First word is lowercase, subsequent words are Title case (camelCase)
            var sb = new System.Text.StringBuilder();
            for (int i = 0; i < parts.Length; i++)
            {
                string part = parts[i];
                if (i == 0)
                {
                    // First word: all lowercase
                    sb.Append(part.ToLowerInvariant());
                }
                else
                {
                    // Subsequent words: capitalize first letter, lowercase rest
                    sb.Append(char.ToUpperInvariant(part[0]));
                    if (part.Length > 1)
                        sb.Append(part.Substring(1).ToLowerInvariant());
                }
            }
            return sb.ToString();
        }
    }
}

