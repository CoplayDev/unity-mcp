using UnityEngine;

namespace AssetStoreTools.Previews.Generators.Custom.Screenshotters
{
    internal interface ISceneScreenshotter
    {
        SceneScreenshotterSettings Settings { get; }

        string Screenshot(string outputPath);
        string Screenshot(GameObject target, string outputPath);
    }
}