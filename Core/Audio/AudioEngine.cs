#nullable enable
using System;
using System.Collections.Generic;
using ManagedBass;
using T3.Core.Animation;
using T3.Core.IO;
using T3.Core.Operator;

namespace T3.Core.Audio;

/// <summary>
/// Controls loading, playback and discarding of audio clips.
/// </summary>
public static class AudioEngine
{
    public static void UseAudioClip(AudioClipResourceHandle handle, double time)
    {
        _updatedClipTimes[handle] = time;
    }

    public static void ReloadClip(AudioClipResourceHandle handle)
    {
        if (ClipStreams.TryGetValue(handle, out var stream))
        {
            Bass.StreamFree(stream.StreamHandle);
            ClipStreams.Remove(handle);
        }

        UseAudioClip(handle, 0);
    }

    public static void CompleteFrame(Playback playback, double frameDurationInSeconds)
    {
        if (!_bassInitialized)
        {
            Bass.Free();
            Bass.Init();
            AudioMixerManager.Initialize();
            _bassInitialized = true;
        }
        
        // For audio-soundtrack we update once every frame. For Wasapi-inputs, we process directly in the new data callback
        if(playback.Settings is { 
               Enabled: true, 
               AudioSource: PlaybackSettings.AudioSources.ProjectSoundTrack })
            AudioAnalysis.ProcessUpdate(playback.Settings.AudioGainFactor, playback.Settings.AudioDecayFactor);

        // Create new streams
        foreach (var (handle, time) in _updatedClipTimes)
        {
            if (ClipStreams.TryGetValue(handle, out var clip))
            {
                clip.TargetTime = time;
            }
            else if(!string.IsNullOrEmpty(handle.Clip.FilePath))
            {
                if (AudioClipStream.TryLoadClip(handle, out var audioClipStream))
                {
                    ClipStreams[handle] = audioClipStream;
                }
            }
        }


        var playbackSpeedChanged = Math.Abs(_lastPlaybackSpeed - playback.PlaybackSpeed) > 0.001f;
        _lastPlaybackSpeed = playback.PlaybackSpeed;

        var handledMainSoundtrack = false;
        foreach (var (handle, clipStream) in ClipStreams)
        {
            clipStream.IsInUse = _updatedClipTimes.ContainsKey(clipStream.ResourceHandle);
            if (!clipStream.IsInUse && clipStream.ResourceHandle.Clip.DiscardAfterUse)
            {
                _obsoleteHandles.Add(handle);
            }
            else
            {
                if (!playback.IsRenderingToFile && playbackSpeedChanged)
                    clipStream.UpdatePlaybackSpeed(playback.PlaybackSpeed);

                if (handledMainSoundtrack || !clipStream.ResourceHandle.Clip.IsSoundtrack)
                    continue;

                handledMainSoundtrack = true;

                if (playback.IsRenderingToFile)
                {
                    AudioRendering.ExportAudioFrame(playback, frameDurationInSeconds, clipStream);
                }
                else
                {
                    UpdateFftBufferFromSoundtrack(clipStream.StreamHandle, playback);
                    clipStream.UpdateTime(playback);
                }
            }
        }

        foreach (var handle in _obsoleteHandles)
        {
            ClipStreams[handle].Disable();
            ClipStreams.Remove(handle);
        }
        
        // Clear after loop to avoid keeping open references
        _obsoleteHandles.Clear();
        _updatedClipTimes.Clear();
    }

    public static void SetMute(bool configAudioMuted)
    {
        IsMuted = configAudioMuted;
    }

    public static bool IsMuted { get; private set; }



    internal static void UpdateFftBufferFromSoundtrack(int soundStreamHandle, Playback playback)
    {
        var dataFlags = (int)DataFlags.FFT2048; // This will return 1024 values
        
        
        // Do not advance playback if we are not in live mode
        if (playback.IsRenderingToFile)
        {
            // ReSharper disable once InconsistentNaming
            const int DataFlag_BASS_DATA_NOREMOVE = 268435456; // Internal id from ManagedBass
            dataFlags |= DataFlag_BASS_DATA_NOREMOVE;
        }

        if (playback.Settings is not { AudioSource: PlaybackSettings.AudioSources.ProjectSoundTrack }) 
            return;
        
        // Get FftGainBuffer
        _ = Bass.ChannelGetData(soundStreamHandle, AudioAnalysis.FftGainBuffer, dataFlags);

        
        // If requested, also fetch WaveFormData 
        if (!WaveFormProcessing.RequestedOnce) 
            return;
        
        const int lengthInBytes = WaveFormProcessing.WaveSampleCount << 2 << 1;
        
        // This will later be processed in WaveFormProcessing
        WaveFormProcessing.LastFetchResultCode = Bass.ChannelGetData(soundStreamHandle, 
                                                                     WaveFormProcessing.InterleavenSampleBuffer,  
                                                                     lengthInBytes);
    }

    public static int GetClipChannelCount(AudioClipResourceHandle? handle)
    {
        // By default, use stereo
        if (handle == null || !ClipStreams.TryGetValue(handle, out var clipStream))
            return 2;

        Bass.ChannelGetInfo(clipStream.StreamHandle, out var info);
        return info.Channels;
    }

    // TODO: Rename to GetClipOrDefaultSampleRate
    public static int GetClipSampleRate(AudioClipResourceHandle? clip)
    {
        if (clip == null || !ClipStreams.TryGetValue(clip, out var stream))
            return 48000;

        Bass.ChannelGetInfo(stream.StreamHandle, out var info);
        return info.Frequency;
    }

    #region Operator Audio Playback
    private static readonly Dictionary<Guid, OperatorAudioState> _operatorAudioStates = new();

    private class OperatorAudioState
    {
        public OperatorAudioStream? Stream;
        public string CurrentFilePath = string.Empty;
        public bool IsPaused;
    }

    public static void UpdateOperatorPlayback(
        Guid operatorId,
        double localFxTime,
        string filePath,
        bool shouldPlay,
        bool shouldStop,
        float volume,
        bool mute,
        float panning)
    {
        if (!_operatorAudioStates.TryGetValue(operatorId, out var state))
        {
            state = new OperatorAudioState();
            _operatorAudioStates[operatorId] = state;
        }

        // Handle file change
        if (state.CurrentFilePath != filePath)
        {
            state.Stream?.Dispose();
            state.Stream = null;
            state.CurrentFilePath = filePath;

            if (!string.IsNullOrEmpty(filePath))
            {
                if (OperatorAudioStream.TryLoadStream(filePath, AudioMixerManager.OperatorMixerHandle, out var stream))
                {
                    state.Stream = stream;
                }
            }
        }

        if (state.Stream == null)
            return;

        // Handle stop
        if (shouldStop)
        {
            state.Stream.Stop();
            state.IsPaused = false;
            return;
        }

        // Handle play
        if (shouldPlay && !state.Stream.IsPlaying)
        {
            state.Stream.Play();
        }
        else if (!shouldPlay && state.Stream.IsPlaying && !state.IsPaused)
        {
            state.Stream.Stop();
        }

        // Update volume, mute, and panning
        state.Stream.SetVolume(volume, mute);
        state.Stream.SetPanning(panning);
    }

    public static void PauseOperator(Guid operatorId)
    {
        if (_operatorAudioStates.TryGetValue(operatorId, out var state) && state.Stream != null)
        {
            state.Stream.Pause();
            state.IsPaused = true;
        }
    }

    public static void ResumeOperator(Guid operatorId)
    {
        if (_operatorAudioStates.TryGetValue(operatorId, out var state) && state.Stream != null)
        {
            state.Stream.Resume();
            state.IsPaused = false;
        }
    }

    public static bool IsOperatorStreamPlaying(Guid operatorId)
    {
        return _operatorAudioStates.TryGetValue(operatorId, out var state) 
               && state.Stream != null 
               && state.Stream.IsPlaying 
               && !state.Stream.IsPaused;
    }

    public static bool IsOperatorPaused(Guid operatorId)
    {
        return _operatorAudioStates.TryGetValue(operatorId, out var state) 
               && state.Stream != null 
               && state.IsPaused;
    }

    public static float GetOperatorLevel(Guid operatorId)
    {
        if (_operatorAudioStates.TryGetValue(operatorId, out var state) && state.Stream != null)
        {
            return state.Stream.GetLevel();
        }
        return 0f;
    }

    public static void UnregisterOperator(Guid operatorId)
    {
        if (_operatorAudioStates.TryGetValue(operatorId, out var state))
        {
            state.Stream?.Dispose();
            _operatorAudioStates.Remove(operatorId);
        }
    }
    #endregion

    private static double _lastPlaybackSpeed = 1;
    private static bool _bassInitialized;
    internal static readonly Dictionary<AudioClipResourceHandle, AudioClipStream> ClipStreams = new();
    private static readonly Dictionary<AudioClipResourceHandle, double> _updatedClipTimes = new();

    // reused list to avoid allocations
    private static readonly List<AudioClipResourceHandle> _obsoleteHandles = [];
}