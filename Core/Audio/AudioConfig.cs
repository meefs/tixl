using ManagedBass;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// Configuration settings for the audio system.
/// Compile-time constants are used for buffer sizes that require static initialization.
/// Runtime settings can be configured through UserSettings in the Editor.
/// </summary>
public static class AudioConfig
{
    #region Logging Configuration (Runtime Configurable)
    /// <summary>
    /// When true, Debug and Info logs from audio classes will be shown.
    /// </summary>
    public static bool ShowAudioLogs { get; set; } = false;

    /// <summary>
    /// When true, Debug and Info logs from audio rendering classes will be shown.
    /// </summary>
    public static bool ShowAudioRenderLogs { get; set; } = false;
    #endregion

    #region Mixer Configuration (Compile-time Constants)
    // Note: These are const because they are used for static array initialization
    // and changing them at runtime would require reallocating audio buffers.

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

    #region FFT and Analysis Configuration (Compile-time Constants)
    // Note: These are const because AudioAnalysis uses them to allocate static arrays.

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

    #region Logging Helpers
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
    #endregion

    #region Initialization
    /// <summary>
    /// Initialize AudioConfig with settings from the Editor.
    /// Call this during application startup after UserSettings is loaded.
    /// </summary>
    /// <param name="showAudioLogs">Enable debug/info logging for audio classes.</param>
    /// <param name="showAudioRenderLogs">Enable debug/info logging for audio rendering classes.</param>
    public static void Initialize(bool showAudioLogs, bool showAudioRenderLogs)
    {
        ShowAudioLogs = showAudioLogs;
        ShowAudioRenderLogs = showAudioRenderLogs;
        
        LogAudioDebug("[AudioConfig] Initialized with ShowAudioLogs=" + showAudioLogs + ", ShowAudioRenderLogs=" + showAudioRenderLogs);
    }
    #endregion
}
