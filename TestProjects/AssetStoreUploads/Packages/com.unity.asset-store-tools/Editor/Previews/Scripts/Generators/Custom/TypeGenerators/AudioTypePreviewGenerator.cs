using AssetStoreTools.Previews.Data;
using AssetStoreTools.Previews.Utility;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Previews.Generators.Custom.TypeGenerators
{
    internal class AudioTypePreviewGenerator : TypePreviewGeneratorBase
    {
        private AudioTypeGeneratorSettings _settings;
        private Texture2D _texture;

        public override event Action<int, int> OnAssetProcessed;

        public AudioTypePreviewGenerator(AudioTypeGeneratorSettings settings) : base(settings)
        {
            _settings = settings;
        }

        public override void ValidateSettings()
        {
            base.ValidateSettings();

            if (_settings.Width <= 0)
                throw new ArgumentException("Width must be larger than 0");

            if (_settings.Height <= 0)
                throw new ArgumentException("Height must be larger than 0");
        }

        protected override IEnumerable<UnityEngine.Object> CollectAssets()
        {
            var assets = new List<UnityEngine.Object>();
            var materialGuids = AssetDatabase.FindAssets("t:audioclip", Settings.InputPaths);
            foreach (var guid in materialGuids)
            {
                var audioClip = AssetDatabase.LoadAssetAtPath<AudioClip>(AssetDatabase.GUIDToAssetPath(guid));

                // Skip nested audio clips
                if (!AssetDatabase.IsMainAsset(audioClip))
                    continue;

                // Skip materials with an error shader
                if (!IsLoadTypeSupported(audioClip))
                {
                    Debug.LogWarning($"Audio clip '{audioClip}' is using a load type which cannot retrieve sample data. Preview will not be generated.");
                    continue;
                }

                assets.Add(audioClip);
            }

            return assets;
        }

        private bool IsLoadTypeSupported(AudioClip audioClip)
        {
            if (audioClip.loadType == AudioClipLoadType.DecompressOnLoad)
                return true;

            return false;
        }

        protected override async Task<List<PreviewMetadata>> GenerateImpl(IEnumerable<UnityEngine.Object> assets)
        {
            var generatedPreviews = new List<PreviewMetadata>();
            var audioClips = assets.ToList();
            for (int i = 0; i < audioClips.Count; i++)
            {
                var audioClip = audioClips[i] as AudioClip;
                if (audioClip != null)
                {
                    var texture = GenerateAudioClipTexture(audioClip);

                    var outputPath = GenerateOutputPathWithExtension(audioClip, _settings.PreviewFileNamingFormat, _settings.Format);
                    var bytes = PreviewConvertUtility.ConvertTexture(texture, _settings.Format);
                    File.WriteAllBytes(outputPath, bytes);
                    generatedPreviews.Add(ObjectToMetadata(audioClip, outputPath));
                }

                OnAssetProcessed?.Invoke(i, audioClips.Count);
                await Task.Yield();
            }

            return generatedPreviews;
        }

        private Texture2D GenerateAudioClipTexture(AudioClip audioClip)
        {
            if (!audioClip.LoadAudioData())
                throw new Exception("Could not load audio data");

            try
            {
                if (_texture == null)
                    _texture = new Texture2D(_settings.Width, _settings.Height);
                else
#if UNITY_2021_3_OR_NEWER || UNITY_2022_1_OR_NEWER || UNITY_2021_2_OR_NEWER
                    _texture.Reinitialize(_settings.Width, _settings.Height);
#else
                    _texture.Resize(_settings.Width, _settings.Height);
#endif

                FillTextureBackground();
                FillTextureForeground(audioClip);

                _texture.Apply();
                return _texture;
            }
            finally
            {
                audioClip.UnloadAudioData();
            }
        }

        private void FillTextureBackground()
        {
            for (int i = 0; i < _texture.width; i++)
            {
                for (int j = 0; j < _texture.height; j++)
                {
                    _texture.SetPixel(i, j, _settings.BackgroundColor);
                }
            }
        }

        private void FillTextureForeground(AudioClip audioClip)
        {
            var channels = CreateChannels(audioClip);

            for (int i = 0; i < channels.Count; i++)
            {
                DrawChannel(channels[i]);
            }
        }

        private List<AudioChannel> CreateChannels(AudioClip audioClip)
        {
            var channelSamples = GetChannelSamples(audioClip);
            var sectionSize = _texture.height / audioClip.channels;

            var channels = new List<AudioChannel>();

            for (int i = 0; i < audioClip.channels; i++)
            {
                var channelMaxY = (_texture.height - 1) - i * sectionSize;
                var channelMinY = _texture.height - (i + 1) * sectionSize;
                var channel = new AudioChannel(channelMinY, channelMaxY, channelSamples[i]);
                channels.Add(channel);
            }

            return channels;
        }

        private List<List<float>> GetChannelSamples(AudioClip audioClip)
        {
            var channelSamples = new List<List<float>>();
            var allSamples = new float[audioClip.samples * audioClip.channels];

            if (!audioClip.GetData(allSamples, 0))
                throw new Exception("Could not retrieve audio samples");

            for (int i = 0; i < audioClip.channels; i++)
            {
                var samples = new List<float>();
                var sampleIndex = i;
                while (sampleIndex < allSamples.Length)
                {
                    samples.Add(allSamples[sampleIndex]);
                    sampleIndex += audioClip.channels;
                }

                channelSamples.Add(samples);
            }

            return channelSamples;
        }

        private void DrawChannel(AudioChannel channel)
        {
            var sectionData = channel.GetCoordinateData(_texture.width);

            foreach (var data in sectionData)
            {
                DrawVerticalColumn(data.X, data.YBaseline, data.YAboveBaseline, data.YBelowBaseline, _settings.SampleColor);
            }
        }

        private void DrawVerticalColumn(int x, int yBaseline, int y1, int y2, Color color)
        {
            _texture.SetPixel(x, yBaseline, color);

            var startIndex = y1 < y2 ? y1 : y2;
            var endIndex = y1 < y2 ? y2 : y1;

            for (int i = startIndex; i < endIndex; i++)
            {
                _texture.SetPixel(x, i, color);
            }
        }
    }
}