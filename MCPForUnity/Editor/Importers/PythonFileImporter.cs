using UnityEngine;
using UnityEditor.AssetImporters;
using System.IO;

namespace MCPForUnity.Editor.Importers
{
    /// <summary>
    /// Custom importer that allows Unity to recognize .py files as TextAssets.
    /// This enables Python files to be selected in the Inspector and used like any other text asset.
    /// </summary>
    [ScriptedImporter(1, "py")]
    public class PythonFileImporter : ScriptedImporter
    {
        public override void OnImportAsset(AssetImportContext ctx)
        {
            var textAsset = new TextAsset(File.ReadAllText(ctx.assetPath));
            ctx.AddObjectToAsset("main obj", textAsset);
            ctx.SetMainObject(textAsset);
        }
    }
}
