using System;
using System.Collections.Generic;

namespace MCPForUnity.Editor.Helpers
{
    /// <summary>
    /// Provides static methods for creating standardized success and error response objects.
    /// Ensures consistent JSON structure for communication back to the Python server.
    /// </summary>
    public static class Response
    {
        /// <summary>
        /// Creates a standardized success response object.
        /// </summary>
        /// <param name="message">A message describing the successful operation.</param>
        /// <param name="data">Optional additional data to include in the response.</param>
        /// <returns>An object representing the success response.</returns>
        public static object Success(string message, object data = null)
        {
            if (data != null)
            {
                return new
                {
                    success = true,
                    message = message,
                    data = data,
                };
            }
            else
            {
                return new { success = true, message = message };
            }
        }

        /// <summary>
        /// Creates a standardized error response object.
        /// </summary>
        /// <param name="errorCodeOrMessage">A message describing the error.</param>
        /// <param name="data">Optional additional data (e.g., error details) to include.</param>
        /// <returns>An object representing the error response.</returns>
        public static object Error(string errorCodeOrMessage, object data = null)
        {
            if (data != null)
            {
                // Note: The key is "error" for error messages, not "message"
                return new
                {
                    success = false,
                    // Preserve original behavior while adding a machine-parsable code field.
                    // If callers pass a code string, it will be echoed in both code and error.
                    code = errorCodeOrMessage,
                    error = errorCodeOrMessage,
                    data = data,
                };
            }
            else
            {
                return new { success = false, code = errorCodeOrMessage, error = errorCodeOrMessage };
            }
        }
    }
}

