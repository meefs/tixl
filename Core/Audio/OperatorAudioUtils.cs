using System;

namespace T3.Core.Audio
{
    /// <summary>
    /// Utility methods for operator audio sources to ensure correct buffer filling and resampling.
    /// </summary>
    public static class OperatorAudioUtils
    {
        /// <summary>
        /// Fills the output buffer for the requested duration, sample rate, and channel count.
        /// If the operator's native sample rate or channel count differs, resampling and up/down-mixing is performed.
        /// </summary>
        /// <param name="renderFunc">A function that fills a temp buffer at the operator's native sample rate and channel count.</param>
        /// <param name="startTime">Start time in seconds.</param>
        /// <param name="duration">Duration in seconds.</param>
        /// <param name="outputBuffer">The output buffer to fill (interleaved, targetChannels, targetSampleRate).</param>
        /// <param name="operatorSampleRate">The operator's native sample rate.</param>
        /// <param name="operatorChannels">The operator's native channel count.</param>
        /// <param name="targetSampleRate">The target (mixer) sample rate.</param>
        /// <param name="targetChannels">The target (mixer) channel count.</param>
        public static void FillAndResample(
            Func<double, double, float[], int> renderFunc,
            double startTime,
            double duration,
            float[] outputBuffer,
            int operatorSampleRate,
            int operatorChannels,
            int targetSampleRate,
            int targetChannels)
        {
            int targetSamples = outputBuffer.Length / targetChannels;
            if (operatorSampleRate == targetSampleRate && operatorChannels == targetChannels)
            {
                // Direct fill
                renderFunc(startTime, duration, outputBuffer);
                return;
            }

            // Render at native rate/channels
            int opSamples = (int)Math.Round(duration * operatorSampleRate);
            float[] temp = new float[opSamples * operatorChannels];
            int written = renderFunc(startTime, duration, temp);
            if (written < opSamples * operatorChannels)
            {
                // Zero pad if not enough samples
                Array.Clear(temp, written, temp.Length - written);
            }

            // Resample and up/down-mix
            LinearResample(temp, opSamples, operatorChannels, outputBuffer, targetSamples, targetChannels);
        }

        /// <summary>
        /// Simple linear resampler and up/down-mixer for float[] audio (interleaved).
        /// </summary>
        public static void LinearResample(
            float[] input, int inputSamples, int inputChannels,
            float[] output, int outputSamples, int outputChannels)
        {
            for (int ch = 0; ch < Math.Min(inputChannels, outputChannels); ch++)
            {
                for (int i = 0; i < outputSamples; i++)
                {
                    float t = (float)i / (outputSamples - 1);
                    float srcPos = t * (inputSamples - 1);
                    int srcIndex = (int)srcPos;
                    float frac = srcPos - srcIndex;
                    int srcBase = srcIndex * inputChannels + ch;
                    int srcNext = Math.Min(srcIndex + 1, inputSamples - 1) * inputChannels + ch;
                    float sampleA = input[srcBase];
                    float sampleB = input[srcNext];
                    output[i * outputChannels + ch] = sampleA + (sampleB - sampleA) * frac;
                }
            }
            // Upmix: fill extra channels with 0
            if (outputChannels > inputChannels)
            {
                for (int ch = inputChannels; ch < outputChannels; ch++)
                    for (int i = 0; i < outputSamples; i++)
                        output[i * outputChannels + ch] = 0f;
            }
            // Downmix: ignore extra input channels
        }
    }
}
