using AssetStoreTools.Previews.Data;
using UnityEngine;

namespace AssetStoreTools.Previews.Generators.Custom.TypeGenerators
{
    internal class AudioTypeGeneratorSettings : TypeGeneratorSettings
    {
        public int Width;
        public int Height;

        public Color SampleColor;
        public Color BackgroundColor;
        public PreviewFormat Format;
    }
}