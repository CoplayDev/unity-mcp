using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;

namespace MCPForUnity.Editor.Services.Transport
{
    /// <summary>
    /// At-most-once execution for commands that carry a <c>request_id</c>.
    ///
    /// The server reuses one request_id for every resend of a logical call (reconnect after a
    /// domain reload, client retry after a lost reply). The ledger records each id when its
    /// command starts and its JSON response when it finishes, so a resend is answered from the
    /// record instead of running the command a second time.
    ///
    /// Entries live in memory and are written to <see cref="SessionState"/> right before an
    /// assembly reload, which is exactly the boundary they must survive. A command that was
    /// still running when the domain unloaded (an async handler cut off by the reload) comes
    /// back as "started" with no response; a resend of it is answered with
    /// <c>code = "outcome_unknown"</c> rather than being executed blindly.
    /// </summary>
    [InitializeOnLoad]
    internal static class CommandLedger
    {
        internal const int MaxEntries = 64;
        internal const int MaxStoredResponseChars = 32 * 1024;
        internal const string OutcomeUnknownCode = "outcome_unknown";
        private const string SessionKey = "MCPForUnity.CommandLedger.v1";

        internal enum Decision
        {
            /// <summary>Run the command (new id, or no id at all).</summary>
            Execute,
            /// <summary>Answer with <c>replayJson</c>; the command already ran or was interrupted.</summary>
            Replay,
            /// <summary>The same id is running right now in this domain; await <c>inFlight</c>.</summary>
            Attach,
        }

        private sealed class Entry
        {
            public string id;
            public string command;
            public string response;   // null while the command is running
            [JsonIgnore] public Task<string> inFlight;
        }

        private static readonly Dictionary<string, Entry> Entries = new();
        private static readonly LinkedList<string> Order = new();
        private static readonly object Lock = new();

        static CommandLedger()
        {
            Restore();
            AssemblyReloadEvents.beforeAssemblyReload += Persist;
        }

        internal static Decision Begin(string requestId, string commandType, out string replayJson, out Task<string> inFlight)
        {
            replayJson = null;
            inFlight = null;
            if (string.IsNullOrEmpty(requestId))
            {
                return Decision.Execute;
            }

            lock (Lock)
            {
                if (Entries.TryGetValue(requestId, out var entry))
                {
                    if (entry.response != null)
                    {
                        replayJson = MarkReplayed(entry.response);
                        return Decision.Replay;
                    }
                    if (entry.inFlight != null)
                    {
                        inFlight = entry.inFlight;
                        return Decision.Attach;
                    }
                    replayJson = OutcomeUnknown(entry.command ?? commandType, requestId);
                    return Decision.Replay;
                }

                Add(new Entry { id = requestId, command = commandType });
                return Decision.Execute;
            }
        }

        /// <summary>Register the task of an async command so a same-domain duplicate can await it.</summary>
        internal static void SetInFlight(string requestId, Task<string> task)
        {
            if (string.IsNullOrEmpty(requestId)) return;
            lock (Lock)
            {
                if (Entries.TryGetValue(requestId, out var entry) && entry.response == null)
                {
                    entry.inFlight = task;
                }
            }
        }

        /// <summary>Record the final JSON response of a command.</summary>
        internal static void Complete(string requestId, string responseJson)
        {
            if (string.IsNullOrEmpty(requestId) || responseJson == null) return;
            lock (Lock)
            {
                if (!Entries.TryGetValue(requestId, out var entry))
                {
                    entry = new Entry { id = requestId };
                    Add(entry);
                }
                entry.response = Compact(responseJson);
                entry.inFlight = null;
            }
        }

        /// <summary>Forget the running task without recording a response (cancelled or timed out).</summary>
        internal static void Abandon(string requestId)
        {
            if (string.IsNullOrEmpty(requestId)) return;
            lock (Lock)
            {
                if (Entries.TryGetValue(requestId, out var entry))
                {
                    entry.inFlight = null;
                }
            }
        }

        internal static void Persist()
        {
            List<Entry> snapshot;
            lock (Lock)
            {
                snapshot = new List<Entry>(Order.Count);
                foreach (var id in Order)
                {
                    snapshot.Add(Entries[id]);
                }
            }
            SessionState.SetString(SessionKey, JsonConvert.SerializeObject(snapshot));
        }

        internal static void Restore()
        {
            lock (Lock)
            {
                Entries.Clear();
                Order.Clear();
                string json = SessionState.GetString(SessionKey, null);
                if (string.IsNullOrEmpty(json)) return;
                try
                {
                    var restored = JsonConvert.DeserializeObject<List<Entry>>(json);
                    if (restored == null) return;
                    foreach (var entry in restored)
                    {
                        if (!string.IsNullOrEmpty(entry?.id)) Add(entry);
                    }
                }
                catch (JsonException)
                {
                    SessionState.EraseString(SessionKey);
                }
            }
        }

        /// <summary>Test hook: drop all in-memory and persisted entries.</summary>
        internal static void Clear()
        {
            lock (Lock)
            {
                Entries.Clear();
                Order.Clear();
            }
            SessionState.EraseString(SessionKey);
        }

        private static void Add(Entry entry)
        {
            if (Entries.ContainsKey(entry.id))
            {
                Order.Remove(entry.id);
            }
            Entries[entry.id] = entry;
            Order.AddLast(entry.id);
            while (Order.Count > MaxEntries)
            {
                Entries.Remove(Order.First.Value);
                Order.RemoveFirst();
            }
        }

        private static string MarkReplayed(string responseJson)
        {
            try
            {
                var obj = JObject.Parse(responseJson);
                obj["replayed"] = true;
                return obj.ToString(Formatting.None);
            }
            catch (JsonException)
            {
                return responseJson;
            }
        }

        /// <summary>
        /// Keep responses small: a replay only has to tell the caller what happened. Large payloads
        /// (inline images, big listings) are reduced to their status and message.
        /// </summary>
        private static string Compact(string responseJson)
        {
            if (responseJson.Length <= MaxStoredResponseChars) return responseJson;
            JObject original;
            try
            {
                original = JObject.Parse(responseJson);
            }
            catch (JsonException)
            {
                original = new JObject();
            }

            var result = original["result"] as JObject;
            var compactResult = new JObject
            {
                ["success"] = result?["success"] ?? true,
                ["message"] = (result?.Value<string>("message") ?? string.Empty)
                    + " (replayed from the command ledger; the original response was too large to keep)",
            };
            var compact = new JObject
            {
                ["status"] = original["status"] ?? "success",
                ["result"] = compactResult,
                ["truncated"] = true,
            };
            if (original["error"] != null) compact["error"] = original["error"];
            return compact.ToString(Formatting.None);
        }

        private static string OutcomeUnknown(string commandType, string requestId)
        {
            return JsonConvert.SerializeObject(new
            {
                status = "error",
                code = OutcomeUnknownCode,
                error = $"Command '{commandType}' (request {requestId}) was still running when the Unity domain " +
                        "reloaded, so it may have been applied partially or not at all. It was not re-run: " +
                        "check the current editor state before retrying.",
                command = commandType,
                replayed = true,
            });
        }
    }
}
