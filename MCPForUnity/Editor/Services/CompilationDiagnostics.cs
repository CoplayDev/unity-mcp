using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.RegularExpressions;
using Newtonsoft.Json;
using UnityEditor;
using UnityEditor.Compilation;

namespace MCPForUnity.Editor.Services
{
    /// <summary>
    /// Structured compiler output of the most recent script compilation: one entry per message
    /// with file, line, column and the CS code, as reported by <see cref="CompilationPipeline"/>.
    ///
    /// A failed compilation leaves the domain in place, so the in-memory list stays valid; a
    /// successful one is followed by a domain reload, so the list is mirrored to SessionState and
    /// restored on load. Either way the result of the last compile is available afterwards.
    /// </summary>
    [InitializeOnLoad]
    internal static class CompilationDiagnostics
    {
        internal const int MaxMessages = 200;
        private const string SessionKey = "MCPForUnity.CompilationDiagnostics.v1";
        private static readonly Regex CodePattern = new(@"\b(CS\d{4})\b", RegexOptions.CultureInvariant);

        internal sealed class Message
        {
            public string severity;
            public string code;
            public string message;
            public string file;
            public int line;
            public int column;
            public string assembly;
        }

        private sealed class Snapshot
        {
            public List<Message> messages = new();
            public long finished_unix_ms;
            public long started_unix_ms;
        }

        private static Snapshot _last = new();
        private static Snapshot _current;

        static CompilationDiagnostics()
        {
            string json = SessionState.GetString(SessionKey, null);
            if (!string.IsNullOrEmpty(json))
            {
                try
                {
                    _last = JsonConvert.DeserializeObject<Snapshot>(json) ?? new Snapshot();
                }
                catch (JsonException)
                {
                    _last = new Snapshot();
                }
            }

            CompilationPipeline.compilationStarted += _ => OnStarted();
            CompilationPipeline.assemblyCompilationFinished += OnAssemblyFinished;
            CompilationPipeline.compilationFinished += _ => OnFinished();
        }

        internal static bool IsCompiling => EditorApplication.isCompiling;

        internal static long LastFinishedUnixMs => _last.finished_unix_ms;

        internal static IReadOnlyList<Message> GetMessages(bool includeWarnings)
        {
            return _last.messages
                .Where(m => includeWarnings || m.severity == "error")
                .ToList();
        }

        internal static object ToResponseData(bool includeWarnings)
        {
            var messages = GetMessages(includeWarnings);
            return new
            {
                is_compiling = IsCompiling,
                last_compile_started_unix_ms = _last.started_unix_ms == 0 ? (long?)null : _last.started_unix_ms,
                last_compile_finished_unix_ms = _last.finished_unix_ms == 0 ? (long?)null : _last.finished_unix_ms,
                error_count = _last.messages.Count(m => m.severity == "error"),
                warning_count = _last.messages.Count(m => m.severity == "warning"),
                messages,
            };
        }

        /// <summary>Records a set of messages as if a compilation had just finished (tests only).</summary>
        internal static void RecordForTests(IEnumerable<CompilerMessage> messages, string assembly)
        {
            OnStarted();
            OnAssemblyFinished(assembly, messages.ToArray());
            OnFinished();
        }

        private static void OnStarted()
        {
            _current = new Snapshot { started_unix_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
        }

        private static void OnAssemblyFinished(string assemblyPath, CompilerMessage[] messages)
        {
            _current ??= new Snapshot { started_unix_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() };
            foreach (var m in messages)
            {
                if (_current.messages.Count >= MaxMessages)
                {
                    break;
                }

                var code = CodePattern.Match(m.message ?? string.Empty);
                _current.messages.Add(new Message
                {
                    severity = m.type == CompilerMessageType.Error ? "error" : "warning",
                    code = code.Success ? code.Groups[1].Value : null,
                    message = m.message,
                    file = string.IsNullOrEmpty(m.file) ? null : m.file.Replace('\\', '/'),
                    line = m.line,
                    column = m.column,
                    assembly = System.IO.Path.GetFileNameWithoutExtension(assemblyPath),
                });
            }
        }

        private static void OnFinished()
        {
            var finished = _current ?? new Snapshot();
            finished.finished_unix_ms = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds();
            // Errors first so a truncated list still shows what blocks compilation.
            finished.messages = finished.messages.OrderBy(m => m.severity == "error" ? 0 : 1).ToList();
            _last = finished;
            _current = null;
            SessionState.SetString(SessionKey, JsonConvert.SerializeObject(_last));
        }
    }
}
