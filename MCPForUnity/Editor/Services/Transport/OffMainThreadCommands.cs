using System;
using MCPForUnity.Editor.Helpers;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace MCPForUnity.Editor.Services.Transport
{
    /// <summary>
    /// Commands answered directly on the transport's receive thread, bypassing
    /// <see cref="TransportCommandDispatcher"/> and therefore Unity's main thread.
    ///
    /// These exist precisely for the case where the main thread is blocked: routing them through
    /// the normal dispatcher would queue them behind the very stall they are meant to report on or
    /// clear, so <c>answer_dialog</c> in particular would deadlock against the dialog it is trying
    /// to dismiss.
    /// </summary>
    internal static class OffMainThreadCommands
    {
        internal const string Liveness = "liveness";
        internal const string AnswerDialog = "answer_dialog";

        internal static bool IsOffMainThreadCommand(string commandType)
        {
            return string.Equals(commandType, Liveness, StringComparison.OrdinalIgnoreCase)
                   || string.Equals(commandType, AnswerDialog, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Handle an off-main-thread command. Returns the serialized transport response envelope.
        /// </summary>
        internal static string Handle(string commandType, JObject parameters)
        {
            object result;
            try
            {
                result = string.Equals(commandType, AnswerDialog, StringComparison.OrdinalIgnoreCase)
                    ? HandleAnswerDialog(parameters ?? new JObject())
                    : HandleLiveness();
            }
            catch (Exception ex)
            {
                result = new ErrorResponse($"{commandType}_failed: {ex.Message}");
            }

            return JsonConvert.SerializeObject(new { status = "success", result });
        }

        /// <summary>
        /// Parse a raw transport frame and handle it when it names an off-main-thread command.
        /// </summary>
        internal static bool TryHandleRaw(string commandText, out string responseJson)
        {
            responseJson = null;
            if (string.IsNullOrWhiteSpace(commandText))
            {
                return false;
            }

            string trimmed = commandText.Trim();
            if (!trimmed.StartsWith("{", StringComparison.Ordinal))
            {
                return false;
            }

            string commandType;
            JObject parameters;
            try
            {
                var parsed = JObject.Parse(trimmed);
                commandType = parsed.Value<string>("type");
                parameters = parsed["params"] as JObject;
            }
            catch
            {
                return false;
            }

            if (string.IsNullOrEmpty(commandType) || !IsOffMainThreadCommand(commandType))
            {
                return false;
            }

            responseJson = Handle(commandType, parameters);
            return true;
        }

        private static object HandleLiveness()
        {
            return new SuccessResponse("Liveness snapshot.", EditorLivenessProbe.Capture());
        }

        private static object HandleAnswerDialog(JObject parameters)
        {
            string button = parameters.Value<string>("button");
            if (string.IsNullOrWhiteSpace(button))
            {
                return new ErrorResponse("Required parameter 'button' is missing.");
            }

            string expectedTitle = parameters.Value<string>("expect_title");

            if (ModalDialogProbe.TryAnswer(expectedTitle, button, out string error, out var observed))
            {
                return new SuccessResponse($"Answered dialog '{observed.Title}' with '{button}'.", new
                {
                    title = observed.Title,
                    button,
                    buttons = observed.Buttons
                });
            }

            return new ErrorResponse(error, new
            {
                reason = observed.Blocked ? "answer_rejected" : "no_dialog_open",
                title = observed.Title,
                buttons = observed.Buttons,
                supported = observed.Supported
            });
        }
    }
}
