#nullable enable

using System;
using T3.Core.Animation;

namespace T3.Core.Audio;

/// <summary>
/// Converts sample data from BASS into a list of buffers that can then be used by Operators like [AudioWaveform].  
/// </summary>
public static class WaveFormProcessing
{
    /// <summary>
    /// This will be set when updating channel audio data from Soundtrack or Wasapi inputs 
    /// </summary>
    internal static readonly float[] InterleavenSampleBuffer = new float[AudioConfig.WaveformSampleCount << 1];

    internal static int LastFetchResultCode;

    /// <summary>
    /// To avoid unnecessary processing we only fetch wave data from BASS when requested once from an Operator. 
    /// </summary>
    internal static bool RequestedOnce;

    /// <summary>
    /// Results of the waveform analysis
    /// </summary>
    public static readonly float[] WaveformLeftBuffer = new float[AudioConfig.WaveformSampleCount];

    public static readonly float[] WaveformRightBuffer = new float[AudioConfig.WaveformSampleCount];
    public static readonly float[] WaveformLowBuffer = new float[AudioConfig.WaveformSampleCount];
    public static readonly float[] WaveformMidBuffer = new float[AudioConfig.WaveformSampleCount];
    public static readonly float[] WaveformHighBuffer = new float[AudioConfig.WaveformSampleCount];

    private static int _lastUpdateFrame = -1;

    /// <summary>
    /// Needs to be called from Operators that want to access Waveform Data.
    /// It will prevent multiple updates per frame.
    /// </summary>
    public static void UpdateWaveformData()
    {
        RequestedOnce = true;

        // Prevent multiple updates
        if (Playback.FrameCount == _lastUpdateFrame)
            return;

        _lastUpdateFrame = Playback.FrameCount;

        //
        if (LastFetchResultCode <= 0)
            return;

        var idx = 0;
        for (var it = 0; it < InterleavenSampleBuffer.Length;)
        {
            WaveformLeftBuffer[idx] = InterleavenSampleBuffer[it++];
            WaveformRightBuffer[idx] = InterleavenSampleBuffer[it++];
            idx += 1;
        }

        // Apply improved filters to create frequency-separated waveforms
        ProcessFilteredWaveformsImproved();
    }

    private struct FilterCoefficients(float a, float b)
    {
        public readonly float A = a;
        public readonly float B = b;
    }

    private static FilterCoefficients CalculateLowPassCoeffs(float cutoffFreq)
    {
        float sampleRate = AudioConfig.MixerFrequency;
        var rc = 1.0f / (2.0f * MathF.PI * cutoffFreq);
        var dt = 1.0f / sampleRate;
        var alpha = dt / (rc + dt);
        return new FilterCoefficients(alpha, 1.0f - alpha);
    }

    private static FilterCoefficients CalculateHighPassCoeffs(float cutoffFreq)
    {
        float sampleRate = AudioConfig.MixerFrequency;
        float rc = 1.0f / (2.0f * MathF.PI * cutoffFreq);
        float dt = 1.0f / sampleRate;
        float alpha = rc / (rc + dt);
        return new FilterCoefficients(alpha, alpha);
    }

    // More efficient single-pole IIR filters with state preservation
    private static void ApplyLowPassFilterImproved(float[] input, float[] output, FilterCoefficients coeffs, ref float y1)
    {
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = coeffs.A * input[i] + coeffs.B * y1;
            y1 = output[i];
        }
    }

    private static void ApplyHighPassFilterImproved(float[] input, float[] output, FilterCoefficients coeffs, ref float y1, ref float x1)
    {
        for (int i = 0; i < input.Length; i++)
        {
            output[i] = coeffs.A * (y1 + input[i] - x1);
            y1 = output[i];
            x1 = input[i];
        }
    }

    private static void ProcessFilteredWaveformsImproved()
    {
        // Create mono mix for filtering (reuse temp buffer)
        for (int i = 0; i < AudioConfig.WaveformSampleCount; i++)
        {
            _tempBuffer[i] = (WaveformLeftBuffer[i] + WaveformRightBuffer[i]) * 0.5f;
        }

        // Apply filters with state preservation for better continuity
        // Low frequencies: Pure low-pass at 250Hz
        ApplyLowPassFilterImproved(_tempBuffer, WaveformLowBuffer, _lowPassCoeffs, ref _lowFilterY1);

        // High frequencies: Pure high-pass at 2000Hz
        ApplyHighPassFilterImproved(_tempBuffer, WaveformHighBuffer, _highPassCoeffs, ref _highFilterY1, ref _highFilterX1);

        // Mid-frequencies: High-pass at 250Hz, then low-pass at 2000Hz (band-pass)
        ApplyHighPassFilterImproved(_tempBuffer, _midFilterBuffer, _midHighPassCoeffs, ref _midHighPassY1, ref _midHighPassX1);
        ApplyLowPassFilterImproved(_midFilterBuffer, WaveformMidBuffer, _midLowPassCoeffs, ref _midLowPassY1);
    }

    // Filter state variables for IIR filters (maintains continuity between frames)
    private static float _lowFilterY1;
    private static float _midHighPassY1;
    private static float _midHighPassX1;
    private static float _midLowPassY1;
    private static float _highFilterY1;
    private static float _highFilterX1;

    // Improved filter coefficients (calculated once)
    private static readonly FilterCoefficients _lowPassCoeffs = CalculateLowPassCoeffs(AudioConfig.LowPassCutoffFrequency);
    private static readonly FilterCoefficients _midHighPassCoeffs = CalculateHighPassCoeffs(AudioConfig.LowPassCutoffFrequency);
    private static readonly FilterCoefficients _midLowPassCoeffs = CalculateLowPassCoeffs(AudioConfig.HighPassCutoffFrequency);
    private static readonly FilterCoefficients _highPassCoeffs = CalculateHighPassCoeffs(AudioConfig.HighPassCutoffFrequency);

    private static readonly float[] _midFilterBuffer = new float[AudioConfig.WaveformSampleCount];
    private static readonly float[] _tempBuffer = new float[AudioConfig.WaveformSampleCount]; // Reusable temp buffer
}