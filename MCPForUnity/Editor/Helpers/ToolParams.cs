using Newtonsoft.Json.Linq;
using System;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Unified parameter validation and extraction wrapper for MCP tools.
    /// Eliminates repetitive IsNullOrEmpty checks and provides consistent error messages.
    /// </summary>
    public class ToolParams
    {
        private readonly JObject _params;

        public ToolParams(JObject @params)
        {
            _params = @params ?? throw new ArgumentNullException(nameof(@params));
        }

        /// <summary>
        /// Get required string parameter. Returns ErrorResponse if missing or empty.
        /// </summary>
        public Result<string> GetRequired(string key, string errorMessage = null)
        {
            var value = GetString(key);
            if (string.IsNullOrEmpty(value))
            {
                return Result<string>.Error(
                    errorMessage ?? $"'{key}' parameter is required."
                );
            }
            return Result<string>.Success(value);
        }

        /// <summary>
        /// Get optional string parameter with default value.
        /// Supports both snake_case and camelCase automatically.
        /// </summary>
        public string Get(string key, string defaultValue = null)
        {
            return GetString(key) ?? defaultValue;
        }

        /// <summary>
        /// Get optional int parameter.
        /// </summary>
        public int? GetInt(string key, int? defaultValue = null)
        {
            var str = GetString(key);
            if (string.IsNullOrEmpty(str)) return defaultValue;
            return int.TryParse(str, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Get optional bool parameter.
        /// Supports both snake_case and camelCase automatically.
        /// </summary>
        public bool GetBool(string key, bool defaultValue = false)
        {
            return ParamCoercion.CoerceBool(GetToken(key), defaultValue);
        }

        /// <summary>
        /// Get optional float parameter.
        /// </summary>
        public float? GetFloat(string key, float? defaultValue = null)
        {
            var str = GetString(key);
            if (string.IsNullOrEmpty(str)) return defaultValue;
            return float.TryParse(str, out var result) ? result : defaultValue;
        }

        /// <summary>
        /// Check if parameter exists (even if null).
        /// Supports both snake_case and camelCase automatically.
        /// </summary>
        public bool Has(string key)
        {
            return GetToken(key) != null;
        }

        /// <summary>
        /// Get raw JToken for complex types.
        /// Supports both snake_case and camelCase automatically.
        /// </summary>
        public JToken GetRaw(string key)
        {
            return GetToken(key);
        }

        /// <summary>
        /// Get raw JToken with snake_case/camelCase fallback.
        /// </summary>
        private JToken GetToken(string key)
        {
            // Try exact match first
            var token = _params[key];
            if (token != null) return token;

            // Try snake_case if camelCase was provided
            var snakeKey = ToSnakeCase(key);
            if (snakeKey != key)
            {
                token = _params[snakeKey];
                if (token != null) return token;
            }

            // Try camelCase if snake_case was provided
            var camelKey = ToCamelCase(key);
            if (camelKey != key)
            {
                token = _params[camelKey];
            }

            return token;
        }

        private string GetString(string key)
        {
            // Try exact match first
            var value = _params[key]?.ToString();
            if (value != null) return value;

            // Try snake_case if camelCase was provided
            var snakeKey = ToSnakeCase(key);
            if (snakeKey != key)
            {
                value = _params[snakeKey]?.ToString();
                if (value != null) return value;
            }

            // Try camelCase if snake_case was provided
            var camelKey = ToCamelCase(key);
            if (camelKey != key)
            {
                value = _params[camelKey]?.ToString();
            }

            return value;
        }

        private static string ToSnakeCase(string str) => StringCaseUtility.ToSnakeCase(str);

        private static string ToCamelCase(string str) => StringCaseUtility.ToCamelCase(str);
    }

    /// <summary>
    /// Result type for operations that can fail with an error message.
    /// </summary>
    public class Result<T>
    {
        public bool IsSuccess { get; }
        public T Value { get; }
        public string ErrorMessage { get; }

        private Result(bool isSuccess, T value, string errorMessage)
        {
            IsSuccess = isSuccess;
            Value = value;
            ErrorMessage = errorMessage;
        }

        public static Result<T> Success(T value) => new Result<T>(true, value, null);
        public static Result<T> Error(string errorMessage) => new Result<T>(false, default, errorMessage);

        /// <summary>
        /// Get value or return ErrorResponse.
        /// </summary>
        public object GetOrError(out T value)
        {
            if (IsSuccess)
            {
                value = Value;
                return null;
            }
            value = default;
            return new ErrorResponse(ErrorMessage);
        }
    }
}
