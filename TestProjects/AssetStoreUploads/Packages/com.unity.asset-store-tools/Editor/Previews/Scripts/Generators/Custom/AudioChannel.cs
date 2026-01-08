using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace AssetStoreTools.Previews.Generators.Custom
{
    internal class AudioChannel
    {
        private int _yMin;
        private int _yMax;

        private int _yBaseline;
        private int _yAmplitude;

        private List<float> _samples;

        public AudioChannel(int minY, int maxY, List<float> samples)
        {
            _yMin = minY;
            _yMax = maxY;

            _yBaseline = (_yMin + _yMax) / 2;
            _yAmplitude = _yMax - _yBaseline;

            _samples = samples;
        }

        public IEnumerable<AudioChannelCoordinate> GetCoordinateData(int desiredWidth)
        {
            var coordinates = new List<AudioChannelCoordinate>();
            var step = Mathf.RoundToInt((float)_samples.Count / desiredWidth);

            for (int i = 0; i < desiredWidth; i++)
            {
                var startIndex = i * step;
                var endIndex = (i + 1) * step;
                var sampleChunk = CreateChunk(startIndex, endIndex);

                if (sampleChunk.Count() == 0)
                    break;

                DownsampleMax(sampleChunk, out var aboveBaseline, out var belowBaseline);

                var yAboveBaseline = SampleToCoordinate(aboveBaseline);
                var yBelowBaseline = SampleToCoordinate(belowBaseline);

                coordinates.Add(new AudioChannelCoordinate(i, _yBaseline, yAboveBaseline, yBelowBaseline));
            }

            // If there weren't enough samples to complete the desired width - fill out the rest with zeroes
            for (int i = coordinates.Count; i < desiredWidth; i++)
                coordinates.Add(new AudioChannelCoordinate(i, _yBaseline, 0, 0));

            return coordinates;
        }

        private IEnumerable<float> CreateChunk(int startIndex, int endIndex)
        {
            var chunk = new List<float>();
            for (int i = startIndex; i < endIndex; i++)
            {
                if (i >= _samples.Count)
                    break;

                chunk.Add(_samples[i]);
            }

            return chunk;
        }

        private void DownsampleMax(IEnumerable<float> samples, out float valueAboveBaseline, out float valueBelowBaseline)
        {
            valueAboveBaseline = 0;
            valueBelowBaseline = 0;

            foreach (var sample in samples)
            {
                if (sample > 0 && sample > valueAboveBaseline)
                {
                    valueAboveBaseline = sample;
                    continue;
                }

                if (sample < 0 && sample < valueBelowBaseline)
                {
                    valueBelowBaseline = sample;
                    continue;
                }
            }
        }

        private int SampleToCoordinate(float sample)
        {
            return _yBaseline + (int)(sample * _yAmplitude);
        }
    }
}