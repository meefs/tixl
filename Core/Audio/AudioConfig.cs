using ManagedBass;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// Configuration settings for the audio system.
/// These are set by the editor based on user preferences.
/// </summary>
public static class AudioConfig
{
    #region Logging Configuration
    /// <summary>
    /// When true, Debug and Info logs from audio classes will be shown.
    /// </summary>
    public static bool ShowAudioLogs { get; set; } = false;

    /// <summary>
    /// When true, Debug and Info logs from audio rendering classes will be shown.
    /// </summary>
    public static bool ShowAudioRenderLogs { get; set; } = false;
    #endregion

    #region Mixer Configuration
    /// <summary>
    /// Sample rate for all mixer streams (Hz).
    /// </summary>
    public const int MixerFrequency = 48000;

    /// <summary>
    /// BASS update period in milliseconds for low-latency playback.
    /// </summary>
    public const int UpdatePeriodMs = 10;

    /// <summary>
    /// Number of BASS update threads.
    /// </summary>
    public const int UpdateThreads = 2;

    /// <summary>
    /// Playback buffer length in milliseconds.
    /// </summary>
    public const int PlaybackBufferLengthMs = 100;

    /// <summary>
    /// Device buffer length in milliseconds for low-latency output.
    /// </summary>
    public const int DeviceBufferLengthMs = 20;
    #endregion

    #region FFT and Analysis Configuration
    /// <summary>
    /// FFT buffer size for frequency analysis.
    /// </summary>
    public const int FftBufferSize = 1024;

    /// <summary>
    /// BASS data flag corresponding to the FFT buffer size.
    /// </summary>
    public const DataFlags BassFftDataFlag = DataFlags.FFT2048;

    /// <summary>
    /// Number of frequency bands for audio analysis.
    /// </summary>
    public const int FrequencyBandCount = 32;

    /// <summary>
    /// Waveform sample buffer size.
    /// </summary>
    public const int WaveformSampleCount = 1024;

    /// <summary>
    /// Low-pass filter cutoff frequency (Hz) for low frequency separation.
    /// </summary>
    public const float LowPassCutoffFrequency = 250f;

    /// <summary>
    /// High-pass filter cutoff frequency (Hz) for high frequency separation.
    /// </summary>
    public const float HighPassCutoffFrequency = 2000f;
    #endregion

    /// <summary>
    /// Helper method to log Debug messages that respect the show setting.
    /// </summary>
    public static void LogAudioDebug(string message)
    {
        if (ShowAudioLogs)
            Log.Debug(message);
    }

    /// <summary>
    /// Helper method to log Info messages that respect the show setting.
    /// </summary>
    public static void LogAudioInfo(string message)
    {
        if (ShowAudioLogs)
            Log.Info(message);
    }

    /// <summary>
    /// Helper method to log Debug messages that respect the show setting.
    /// </summary>
    public static void LogAudioRenderDebug(string message)
    {
        if (ShowAudioRenderLogs)
            Log.Debug(message);
    }

    /// <summary>
    /// Helper method to log Info messages that respect the show setting.
    /// </summary>
    public static void LogAudioRenderInfo(string message)
    {
        if (ShowAudioRenderLogs)
            Log.Info(message);
    }
}
