#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Logging;
using T3.Core.Resource;

namespace T3.Core.Audio;

/// <summary>
/// Represents an audio stream for operator-based playback (plays at normal speed, not synchronized to timeline)
/// </summary>
public sealed class OperatorAudioStream
{
    private OperatorAudioStream()
    {
    }

    public double Duration;
    public int StreamHandle;
    public int MixerStreamHandle;
    public bool IsPaused;
    public bool IsPlaying;
    private float DefaultPlaybackFrequency { get; set; }
    private string FilePath = string.Empty;
    private float _currentVolume = 1.0f;
    private float _currentPanning = 0.0f;

    internal static bool TryLoadStream(string filePath, int mixerHandle, [NotNullWhen(true)] out OperatorAudioStream? stream)
    {
        stream = null;

        if (string.IsNullOrEmpty(filePath))
            return false;

        if (!File.Exists(filePath))
        {
            Log.Error($"Audio file '{filePath}' does not exist.");
            return false;
        }

        var streamHandle = Bass.CreateStream(filePath, 0, 0, BassFlags.Decode | BassFlags.Float);

        if (streamHandle == 0)
        {
            Log.Error($"Error loading audio stream '{filePath}': {Bass.LastError}.");
            return false;
        }

        Bass.ChannelGetAttribute(streamHandle, ChannelAttribute.Frequency, out var defaultPlaybackFrequency);

        var bytes = Bass.ChannelGetLength(streamHandle);
        if (bytes < 0)
        {
            Log.Error($"Failed to get length for audio stream {filePath}.");
            Bass.StreamFree(streamHandle);
            return false;
        }

        var duration = Bass.ChannelBytes2Seconds(streamHandle, bytes);

        // Add stream to mixer
        if (!BassMix.MixerAddChannel(mixerHandle, streamHandle, BassFlags.MixerChanNoRampin))
        {
            Log.Error($"Failed to add stream to mixer: {Bass.LastError}");
            Bass.StreamFree(streamHandle);
            return false;
        }

        stream = new OperatorAudioStream
                     {
                         StreamHandle = streamHandle,
                         MixerStreamHandle = mixerHandle,
                         DefaultPlaybackFrequency = defaultPlaybackFrequency,
                         Duration = duration,
                         FilePath = filePath,
                         IsPlaying = false,
                         IsPaused = false
                     };

        return true;
    }

    public void Play()
    {
        if (IsPlaying && !IsPaused)
            return;

        Bass.ChannelPlay(StreamHandle, false);
        IsPlaying = true;
        IsPaused = false;
    }

    public void Pause()
    {
        if (!IsPlaying || IsPaused)
            return;

        Bass.ChannelPause(StreamHandle);
        IsPaused = true;
    }

    public void Resume()
    {
        if (!IsPaused)
            return;

        Bass.ChannelPlay(StreamHandle, false);
        IsPaused = false;
    }

    public void Stop()
    {
        Bass.ChannelStop(StreamHandle);
        Bass.ChannelSetPosition(StreamHandle, 0);
        IsPlaying = false;
        IsPaused = false;
    }

    public void SetVolume(float volume, bool mute)
    {
        _currentVolume = volume;
        var effectiveVolume = mute ? 0f : volume;
        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Volume, effectiveVolume);
    }

    public void SetPanning(float panning)
    {
        _currentPanning = panning;
        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Pan, panning);
    }

    public void Seek(float timeInSeconds)
    {
        var position = Bass.ChannelSeconds2Bytes(StreamHandle, timeInSeconds);
        Bass.ChannelSetPosition(StreamHandle, position);
    }

    public float GetLevel()
    {
        var levels = Bass.ChannelGetLevel(StreamHandle);
        if (levels == -1)
            return 0f;

        // Extract left and right levels from the 32-bit value
        var left = (levels & 0xFFFF) / 32768f;
        var right = ((levels >> 16) & 0xFFFF) / 32768f;
        return Math.Max(left, right);
    }

    public void Dispose()
    {
        BassMix.MixerRemoveChannel(StreamHandle);
        Bass.StreamFree(StreamHandle);
    }
}
