using System.Collections.Generic;
using System.Linq;

namespace MCPForUnity.Editor.Tools.Sprite2D
{
    internal class SpriteDiagnostic
    {
        public string code;
        public string severity;
        public string message;
        public object detail;
        public string[] fix_options;
    }

    internal class SpriteDiagnosticBuilder
    {
        private readonly List<SpriteDiagnostic> _list = new List<SpriteDiagnostic>();
        public bool HasErrors => _list.Any(d => d.severity == "error");

        public void AddError(string code, string message, object detail, string[] fixes) =>
            _list.Add(new SpriteDiagnostic { code = code, severity = "error", message = message, detail = detail, fix_options = fixes });

        public void AddWarning(string code, string message, object detail, string[] fixes) =>
            _list.Add(new SpriteDiagnostic { code = code, severity = "warning", message = message, detail = detail, fix_options = fixes });

        public void AddInfo(string code, string message, object detail) =>
            _list.Add(new SpriteDiagnostic { code = code, severity = "info", message = message, detail = detail, fix_options = new string[0] });

        public List<SpriteDiagnostic> Build() => new List<SpriteDiagnostic>(_list);
    }
}
