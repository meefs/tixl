#nullable enable
using System;
using System.Collections.Generic;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Logging;

namespace T3.Core.Audio;

/// <summary>
/// Manages the audio mixer architecture with separate paths for operator clips and soundtrack clips
/// Architecture: Operator Clip(s) > Operator Mixer > Global Mixer > Soundcard
///               Soundtrack Clip(s) > Soundtrack Mixer > Global Mixer > Soundcard
/// </summary>
public static class AudioMixerManager
{
    private static int _globalMixerHandle;
    private static int _operatorMixerHandle;
    private static int _soundtrackMixerHandle;
    private static bool _initialized;
    private static int _flacPluginHandle;

    public static int GlobalMixerHandle => _globalMixerHandle;
    public static int OperatorMixerHandle => _operatorMixerHandle;
    public static int SoundtrackMixerHandle => _soundtrackMixerHandle;

    public static void Initialize()
    {
        if (_initialized)
            return;

        // Check if BASS is already initialized
        DeviceInfo deviceInfo;
        var bassIsInitialized = Bass.GetDeviceInfo(Bass.CurrentDevice, out deviceInfo) && deviceInfo.IsInitialized;
        
        if (!bassIsInitialized)
        {
            if (!Bass.Init(-1, 44100, DeviceInitFlags.Default, IntPtr.Zero))
            {
                Log.Error($"Failed to initialize BASS: {Bass.LastError}");
                return;
            }
        }

        // Load BASS FLAC plugin for native FLAC support (better than Media Foundation)
        // The plugin DLL should be in the same directory as bassflac.dll or bass.dll
        _flacPluginHandle = Bass.PluginLoad("bassflac.dll");
        if (_flacPluginHandle == 0)
        {
            Log.Warning($"Failed to load BASS FLAC plugin: {Bass.LastError}. FLAC files will use Media Foundation fallback.");
        }
        else
        {
            Log.Debug($"BASS FLAC plugin loaded successfully: {_flacPluginHandle}");
        }

        // Create global mixer (stereo output to soundcard)
        _globalMixerHandle = BassMix.CreateMixerStream(44100, 2, BassFlags.Float | BassFlags.MixerNonStop);
        if (_globalMixerHandle == 0)
        {
            Log.Error($"Failed to create global mixer: {Bass.LastError}");
            return;
        }

        // Create operator mixer (decode stream that feeds into global mixer)
        _operatorMixerHandle = BassMix.CreateMixerStream(44100, 2, BassFlags.MixerNonStop | BassFlags.Decode);
        if (_operatorMixerHandle == 0)
        {
            Log.Error($"Failed to create operator mixer: {Bass.LastError}");
            return;
        }

        // Create soundtrack mixer (decode stream that feeds into global mixer)
        _soundtrackMixerHandle = BassMix.CreateMixerStream(44100, 2, BassFlags.MixerNonStop | BassFlags.Decode);
        if (_soundtrackMixerHandle == 0)
        {
            Log.Error($"Failed to create soundtrack mixer: {Bass.LastError}");
            return;
        }

        // Add operator mixer to global mixer
        if (!BassMix.MixerAddChannel(_globalMixerHandle, _operatorMixerHandle, BassFlags.Default))
        {
            Log.Error($"Failed to add operator mixer to global mixer: {Bass.LastError}");
        }

        // Add soundtrack mixer to global mixer
        if (!BassMix.MixerAddChannel(_globalMixerHandle, _soundtrackMixerHandle, BassFlags.Default))
        {
            Log.Error($"Failed to add soundtrack mixer to global mixer: {Bass.LastError}");
        }

        // IMPORTANT: Do NOT call Bass.ChannelPlay() on decode streams!
        // Decode streams are passive data sources that are pulled from by the mixer.
        // Only the global mixer (output stream) needs to be started.

        // Start the global mixer playing (outputs to soundcard)
        if (!Bass.ChannelPlay(_globalMixerHandle, false))
        {
            Log.Error($"Failed to start global mixer: {Bass.LastError}");
        }

        _initialized = true;
        Log.Debug("Audio mixer system initialized successfully.");
    }

    public static void Shutdown()
    {
        if (!_initialized)
            return;

        Bass.StreamFree(_operatorMixerHandle);
        Bass.StreamFree(_soundtrackMixerHandle);
        Bass.StreamFree(_globalMixerHandle);
        
        // Unload FLAC plugin
        if (_flacPluginHandle != 0)
        {
            Bass.PluginFree(_flacPluginHandle);
        }
        
        Bass.Free();

        _initialized = false;
        Log.Debug("Audio mixer system shut down.");
    }

    public static void SetOperatorMixerVolume(float volume)
    {
        if (!_initialized) return;
        Bass.ChannelSetAttribute(_operatorMixerHandle, ChannelAttribute.Volume, volume);
    }

    public static void SetSoundtrackMixerVolume(float volume)
    {
        if (!_initialized) return;
        Bass.ChannelSetAttribute(_soundtrackMixerHandle, ChannelAttribute.Volume, volume);
    }

    public static void SetGlobalVolume(float volume)
    {
        if (!_initialized) return;
        Bass.ChannelSetAttribute(_globalMixerHandle, ChannelAttribute.Volume, volume);
    }
}
