using System.Collections.Generic;
using System.Linq;

namespace MCPForUnity.Editor.Tools.Sprite2D
{
    internal class SpriteDiagnostic
    {
        public string code;
        public string severity;
        public string message;
        public string[] fix_options;
    }

    internal class SpriteDiagnosticBuilder
    {
        private readonly List<SpriteDiagnostic> _list = new List<SpriteDiagnostic>();

        public bool HasErrors => _list.Any(d => d.severity == "error");

        public string FirstError => _list.FirstOrDefault(d => d.severity == "error")?.message;

        public void AddError(string code, string message, params string[] fixes) =>
            Add(code, "error", message, fixes);

        public void AddWarning(string code, string message, params string[] fixes) =>
            Add(code, "warning", message, fixes);

        public object Fail(string code, string message, params string[] fixes)
        {
            AddError(code, message, fixes);
            return Fail();
        }

        public object Fail() =>
            new { success = false, message = FirstError, diagnostics = Build() };

        public List<SpriteDiagnostic> Build() => new List<SpriteDiagnostic>(_list);

        private void Add(string code, string severity, string message, params string[] fixes) =>
            _list.Add(new SpriteDiagnostic { code = code, severity = severity, message = message, fix_options = fixes });
    }
}
