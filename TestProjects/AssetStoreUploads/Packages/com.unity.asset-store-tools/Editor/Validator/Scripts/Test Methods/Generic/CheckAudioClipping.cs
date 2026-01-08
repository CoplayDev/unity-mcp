using AssetStoreTools.Validator.Data;
using AssetStoreTools.Validator.Services.Validation;
using AssetStoreTools.Validator.TestDefinitions;
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace AssetStoreTools.Validator.TestMethods
{
    internal class CheckAudioClipping : ITestScript
    {
        private GenericTestConfig _config;
        private IAssetUtilityService _assetUtility;

        public CheckAudioClipping(GenericTestConfig config, IAssetUtilityService assetUtility)
        {
            _config = config;
            _assetUtility = assetUtility;
        }

        public TestResult Run()
        {
            // How many peaks above threshold are required for Audio Clips to be considered clipping
            const int TOLERANCE = 2;
            // Min. amount of consecutive samples above threshold required for peak detection
            const int PEAK_STEPS = 1;
            // Clipping threshold. More lenient here than Submission Guidelines (-0.3db) due to the problematic nature of 
            // correctly determining how sensitive the audio clipping flagging should be, as well as to account for any
            // distortion introduced when AudioClips are compresssed after importing to Unity.
            const float THRESHOLD = -0.05f;
            // Samples for 16-bit audio files
            const float S16b = 32767f;
            float clippingThreshold = (S16b - (S16b / (2 * Mathf.Log10(1 / S16b)) * THRESHOLD)) / S16b;
            TestResult result = new TestResult();
            var clippingAudioClips = new Dictionary<AudioClip, string>();

            var losslessAudioClips = _assetUtility.GetObjectsFromAssets(_config.ValidationPaths, AssetType.NonLossyAudio).Select(x => x as AudioClip).ToList();
            foreach (var clip in losslessAudioClips)
            {
                var path = AssetDatabase.GetAssetPath(clip.GetInstanceID());

                if (IsClipping(clip, TOLERANCE, PEAK_STEPS, clippingThreshold))
                    clippingAudioClips.Add(clip, path);
            }

            var lossyAudioClips = _assetUtility.GetObjectsFromAssets(_config.ValidationPaths, AssetType.LossyAudio).Select(x => x as AudioClip).ToList();
            foreach (var clip in lossyAudioClips)
            {
                var path = AssetDatabase.GetAssetPath(clip.GetInstanceID());

                if (IsClipping(clip, TOLERANCE, PEAK_STEPS, clippingThreshold))
                    clippingAudioClips.Add(clip, path);
            }

            if (clippingAudioClips.Count > 0)
            {
                result.Status = TestResultStatus.VariableSeverityIssue;
                result.AddMessage("The following AudioClips are clipping or are very close to 0db ceiling. Please ensure your exported audio files have at least 0.3db of headroom (should peak at no more than -0.3db):", null, clippingAudioClips.Select(x => x.Key).ToArray());
            }
            else
            {
                result.Status = TestResultStatus.Pass;
                result.AddMessage("No clipping audio files were detected.");
            }

            return result;
        }

        private bool IsClipping(AudioClip clip, int tolerance, int peakTolerance, float clippingThreshold)
        {
            if (DetectNumPeaksAboveThreshold(clip, peakTolerance, clippingThreshold) >= tolerance)
                return true;

            return false;
        }

        private int DetectNumPeaksAboveThreshold(AudioClip clip, int peakTolerance, float clippingThreshold)
        {
            float[] samples = new float[clip.samples * clip.channels];
            var data = clip.GetData(samples, 0);

            float[] samplesLeft = samples.Where((s, i) => i % 2 == 0).ToArray();
            float[] samplesRight = samples.Where((s, i) => i % 2 == 1).ToArray();

            int peaks = 0;

            peaks = GetPeaksInChannel(samplesLeft, peakTolerance, clippingThreshold) +
                    GetPeaksInChannel(samplesRight, peakTolerance, clippingThreshold);

            return peaks;
        }

        private int GetPeaksInChannel(float[] samples, int peakTolerance, float clippingThreshold)
        {
            int peaks = 0;
            bool evalPeak = false;
            int peakSteps = 0;
            int step = 0;

            while (step < samples.Length)
            {
                if (Mathf.Abs(samples[step]) >= clippingThreshold && evalPeak)
                {
                    peakSteps++;
                }

                if (Mathf.Abs(samples[step]) >= clippingThreshold && !evalPeak)
                {
                    evalPeak = true;
                    peakSteps++;
                }

                if (Mathf.Abs(samples[step]) < clippingThreshold && evalPeak)
                {
                    evalPeak = false;
                    if (peakSteps >= peakTolerance)
                        peaks++;
                    peakSteps = 0;
                }

                step++;
            }

            return peaks;
        }
    }
}
