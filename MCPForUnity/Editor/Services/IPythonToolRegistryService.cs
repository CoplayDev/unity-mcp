using System.Collections.Generic;
using UnityEngine;
using MCPForUnity.Editor.Data;

namespace MCPForUnity.Editor.Services
{
    public interface IPythonToolRegistryService
    {
        IEnumerable<PythonToolsAsset> GetAllRegistries();
        bool NeedsSync(PythonToolsAsset registry, TextAsset file);
        void RecordSync(PythonToolsAsset registry, TextAsset file);
        string ComputeHash(TextAsset file);
    }
}