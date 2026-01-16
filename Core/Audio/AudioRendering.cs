using System;
using System.Collections.Generic;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Animation;
using T3.Core.Logging;
using T3.Core.Operator;

namespace T3.Core.Audio;

/// <summary>
/// Handles audio rendering/export functionality.
/// 
/// For export, we temporarily remove soundtrack streams from the mixer and read directly from them.
/// This avoids all mixer buffering issues.
/// </summary>
public static class AudioRendering
{
    private static bool _isRecording;
    private static ExportState _exportState = new();
    private static double _exportStartTime;
    private static int _frameCount;

    /// <summary>
    /// Prepares the audio system for recording/export.
    /// </summary>
    public static void PrepareRecording(Playback playback, double fps)
    {
        if (_isRecording)
            return;

        _isRecording = true;
        _frameCount = 0;
        _exportStartTime = playback.TimeInSecs;
        
        _exportState.SaveState();
        AudioExportSourceRegistry.Clear();
        
        Bass.ChannelPause(AudioMixerManager.GlobalMixerHandle);
        AudioConfig.LogAudioRenderDebug("[AudioRendering] GlobalMixer PAUSED for export");

        AudioEngine.ResetAllOperatorStreamsForExport();

        // Remove soundtrack streams from mixer for direct reading
        foreach (var (handle, clipStream) in AudioEngine.SoundtrackClipStreams)
        {
            float nativeFrequency = clipStream.GetDefaultFrequency();
            Bass.ChannelGetInfo(clipStream.StreamHandle, out var clipInfo);

            AudioConfig.LogAudioRenderDebug($"[AudioRendering] Soundtrack clip: File='{handle.Clip.FilePath}', NativeFreq={nativeFrequency}, Channels={clipInfo.Channels}");
            
            // REMOVE from mixer
            BassMix.MixerRemoveChannel(clipStream.StreamHandle);
            AudioConfig.LogAudioRenderDebug("[AudioRendering] Soundtrack REMOVED from mixer for export");
            
            // Reset stream settings
            Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.Frequency, nativeFrequency);
            Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.Volume, handle.Clip.Volume);
            Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.NoRamp, 1);
            Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.ReverseDirection, 1);
        }

        AudioConfig.LogAudioRenderDebug($"[AudioRendering] PrepareRecording: exportStartTime={_exportStartTime:F3}s, fps={fps}");
    }

    /// <summary>
    /// Ends recording and restores the audio system to live playback state.
    /// </summary>
    public static void EndRecording(Playback playback, double fps)
    {
        if (!_isRecording)
            return;

        _isRecording = false;
        AudioConfig.LogAudioRenderDebug($"[AudioRendering] EndRecording: Exported {_frameCount} frames");

        // Re-add soundtrack streams to mixer for live playback
        // Note: We do NOT use MixerChanBuffer for soundtracks - we need accurate position tracking
        foreach (var (handle, clipStream) in AudioEngine.SoundtrackClipStreams)
        {
            if (!BassMix.MixerAddChannel(AudioMixerManager.SoundtrackMixerHandle, clipStream.StreamHandle,
                    BassFlags.MixerChanPause))
            {
                Log.Warning($"[AudioRendering] Failed to re-add soundtrack to mixer: {Bass.LastError}");
            }
            else
            {
                AudioConfig.LogAudioRenderDebug("[AudioRendering] Soundtrack RE-ADDED to mixer for live playback");
            }
            
            clipStream.UpdateTimeWhileRecording(playback, fps, true);
        }

        _exportState.RestoreState();
        AudioEngine.RestoreOperatorAudioStreams();
    }

    /// <summary>
    /// Exports a single audio frame for the given clip stream.
    /// Updates FFT analysis for operators that need waveform/spectrum data.
    /// </summary>
    internal static void ExportAudioFrame(Playback playback, double frameDurationInSeconds, SoundtrackClipStream clipStream)
    {
        try
        {
            // Update FFT analysis - uses BASS_DATA_NOREMOVE flag during export
            // so it doesn't consume audio data from the stream
            AudioEngine.UpdateFftBufferFromSoundtrack(clipStream.StreamHandle, playback);
        }
        catch (Exception ex)
        {
            Log.Error($"ExportAudioFrame error: {ex.Message}", typeof(AudioRendering));
        }
    }

    /// <summary>
    /// Gets the full mixed audio buffer for export.
    /// Reads directly from source streams (not through mixer) and handles resampling.
    /// </summary>
    public static float[] GetFullMixDownBuffer(double frameDurationInSeconds, double localFxTime)
    {
        _frameCount++;
        
        AudioEngine.UpdateStaleStatesForExport();
        
        int mixerSampleRate = AudioConfig.MixerFrequency;
        int channels = 2;
        int sampleCount = (int)Math.Max(Math.Round(frameDurationInSeconds * mixerSampleRate), 1);
        int floatCount = sampleCount * channels;
        float[] mixBuffer = new float[floatCount];

        double currentTimeInSeconds = Playback.Current.TimeInSecs;

        // Read directly from each soundtrack stream
        foreach (var (handle, clipStream) in AudioEngine.SoundtrackClipStreams)
        {
            double clipStartInSeconds = Playback.Current.SecondsFromBars(handle.Clip.StartTime);
            double timeInClip = currentTimeInSeconds - clipStartInSeconds;
            double clipLength = handle.Clip.LengthInSeconds;
            
            if (timeInClip < 0 || timeInClip > clipLength)
                continue;
            
            // Get the actual channel count from the stream info
            Bass.ChannelGetInfo(clipStream.StreamHandle, out var streamInfo);
            int nativeChannels = streamInfo.Channels;
            float nativeFreq = clipStream.GetDefaultFrequency();
            
            // Always seek to the exact position for each frame
            // This ensures we read the correct audio data regardless of any drift
            long targetBytes = Bass.ChannelSeconds2Bytes(clipStream.StreamHandle, timeInClip);
            Bass.ChannelSetPosition(clipStream.StreamHandle, targetBytes, PositionFlags.Bytes);
            
            if (_frameCount <= 5)
            {
                var actualPos = Bass.ChannelGetPosition(clipStream.StreamHandle);
                var actualPosSec = Bass.ChannelBytes2Seconds(clipStream.StreamHandle, actualPos);
                AudioConfig.LogAudioRenderDebug($"[AudioRendering] Frame {_frameCount}: target={timeInClip:F4}s, actual={actualPosSec:F4}s, nativeChannels={nativeChannels}");
            }
            
            // Calculate source samples needed based on native sample rate
            int sourceSampleCount = (int)Math.Ceiling(frameDurationInSeconds * nativeFreq);
            int sourceFloatCount = sourceSampleCount * nativeChannels;
            int sourceBytesToRead = sourceFloatCount * sizeof(float);
            
            float[] sourceBuffer = new float[sourceFloatCount];
            
            // Read directly from stream
            int bytesRead = Bass.ChannelGetData(clipStream.StreamHandle, sourceBuffer, sourceBytesToRead);
            
            if (bytesRead > 0)
            {
                int floatsRead = bytesRead / sizeof(float);
                float volume = handle.Clip.Volume;
                
                ResampleAndMix(sourceBuffer, floatsRead, nativeFreq, nativeChannels,
                              mixBuffer, floatCount, mixerSampleRate, channels, volume);
            }
        }

        // Read operators from OperatorMixer
        int bytesToRead = floatCount * sizeof(float);
        float[] operatorBuffer = new float[floatCount];
        int operatorBytesRead = Bass.ChannelGetData(AudioMixerManager.OperatorMixerHandle, operatorBuffer, bytesToRead);
        
        if (operatorBytesRead > 0)
        {
            int samplesRead = operatorBytesRead / sizeof(float);
            for (int i = 0; i < Math.Min(samplesRead, mixBuffer.Length); i++)
            {
                if (!float.IsNaN(operatorBuffer[i]))
                    mixBuffer[i] += operatorBuffer[i];
            }
        }

        if (_frameCount <= 3 || _frameCount % 60 == 0)
        {
            float mixPeak = 0;
            for (int i = 0; i < floatCount; i++)
            {
                if (!float.IsNaN(mixBuffer[i]))
                    mixPeak = Math.Max(mixPeak, Math.Abs(mixBuffer[i]));
            }
            AudioConfig.LogAudioRenderDebug($"[AudioRendering] Frame {_frameCount}: Mix peak={mixPeak:F4}, time={currentTimeInSeconds:F3}s");
        }

        UpdateOperatorMetering();

        return mixBuffer;
    }

    /// <summary>
    /// Resamples audio from source sample rate to target sample rate and mixes into output buffer.
    /// </summary>
    private static void ResampleAndMix(float[] source, int sourceFloatCount, float sourceRate, int sourceChannels,
                                        float[] target, int targetFloatCount, int targetRate, int targetChannels,
                                        float volume)
    {
        int targetSampleCount = targetFloatCount / targetChannels;
        int sourceSampleCount = sourceFloatCount / sourceChannels;
        double ratio = sourceRate / targetRate;
        
        for (int t = 0; t < targetSampleCount; t++)
        {
            double sourcePos = t * ratio;
            int s0 = (int)sourcePos;
            int s1 = s0 + 1;
            double frac = sourcePos - s0;
            
            for (int c = 0; c < targetChannels; c++)
            {
                int sourceChannel = c % sourceChannels;
                
                float v0 = 0, v1 = 0;
                int idx0 = s0 * sourceChannels + sourceChannel;
                int idx1 = s1 * sourceChannels + sourceChannel;
                
                if (idx0 >= 0 && idx0 < sourceFloatCount)
                    v0 = source[idx0];
                if (idx1 >= 0 && idx1 < sourceFloatCount)
                    v1 = source[idx1];
                
                float interpolated = (float)(v0 * (1.0 - frac) + v1 * frac);
                int targetIdx = t * targetChannels + c;
                
                if (targetIdx < target.Length && !float.IsNaN(interpolated))
                    target[targetIdx] += interpolated * volume;
            }
        }
    }

    private static void UpdateOperatorMetering()
    {
        foreach (var kvp in AudioEngine.GetAllStereoOperatorStates())
        {
            var stream = kvp.Value.Stream;
            var isStale = kvp.Value.IsStale;
            
            if (stream == null || !stream.IsPlaying || stream.IsPaused || isStale)
                continue;
            
            var level = BassMix.ChannelGetLevel(stream.StreamHandle);
            if (level != -1)
            {
                float left = (level & 0xFFFF) / 32768f;
                float right = ((level >> 16) & 0xFFFF) / 32768f;
                float[] meteringBuffer = [left, right];
                stream.UpdateFromBuffer(meteringBuffer);
            }
        }
        
        foreach (var kvp in AudioEngine.GetAllSpatialOperatorStates())
        {
            var stream = kvp.Value.Stream;
            var isStale = kvp.Value.IsStale;
            
            if (stream == null || !stream.IsPlaying || stream.IsPaused || isStale)
                continue;
            
            var level = BassMix.ChannelGetLevel(stream.StreamHandle);
            if (level != -1)
            {
                float left = (level & 0xFFFF) / 32768f;
                float right = ((level >> 16) & 0xFFFF) / 32768f;
                float[] meteringBuffer = [left, right];
                stream.UpdateFromBuffer(meteringBuffer);
            }
        }
    }

    public static void GetLastMixDownBuffer(double frameDurationInSeconds)
    {
        foreach (var (_, clipStream) in AudioEngine.SoundtrackClipStreams)
        {
            AudioEngine.UpdateFftBufferFromSoundtrack(clipStream.StreamHandle, Playback.Current);
        }
    }

    public static void EvaluateAllAudioMeteringOutputs(double localFxTime, float[]? audioBuffer = null)
    {
        var context = new EvaluationContext { LocalFxTime = localFxTime };
        
        foreach (var source in AudioExportSourceRegistry.Sources)
        {
            if (source is Instance operatorInstance)
            {
                foreach (var input in operatorInstance.Inputs)
                    input.DirtyFlag.ForceInvalidate();
                
                foreach (var output in operatorInstance.Outputs)
                {
                    try { output.Update(context); }
                    catch (Exception ex)
                    {
                        AudioConfig.LogAudioRenderDebug($"Failed to evaluate output slot: {ex.Message}");
                    }
                }
            }
        }
    }

    private class ExportState
    {
        private float _savedGlobalMixerVolume;
        private bool _wasPlaying;

        public void SaveState()
        {
            Bass.ChannelGetAttribute(AudioMixerManager.GlobalMixerHandle, ChannelAttribute.Volume, out _savedGlobalMixerVolume);
            if (_savedGlobalMixerVolume <= 0)
                _savedGlobalMixerVolume = 1.0f;
            _wasPlaying = Bass.ChannelIsActive(AudioMixerManager.GlobalMixerHandle) == PlaybackState.Playing;
        }

        public void RestoreState()
        {
            Bass.ChannelSetAttribute(AudioMixerManager.GlobalMixerHandle, ChannelAttribute.Volume, _savedGlobalMixerVolume);
            if (_wasPlaying)
            {
                Bass.ChannelPlay(AudioMixerManager.GlobalMixerHandle, false);
                AudioConfig.LogAudioRenderDebug("[AudioRendering] GlobalMixer RESUMED after export");
            }
        }
    }
}