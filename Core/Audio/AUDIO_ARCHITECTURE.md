# Audio System Architecture

**Version:** 1.2  
**Last Updated:** 2025-01-10  
**Status:** Production Ready

---

## Table of Contents
1. [Introduction](#introduction)
2. [Architecture Overview](#architecture-overview)
3. [Audio Operators](#audio-operators)
4. [Configuration System](#configuration-system)
5. [Performance Characteristics](#performance-characteristics)
6. [Documentation Index](#documentation-index)
7. [Future Enhancement Opportunities](#future-enhancement-opportunities)

---

## Introduction

The T3 audio system is a high-performance, low-latency audio engine built on ManagedBass, supporting stereo and 3D spatial audio playback within operator graphs.

### Key Features
- **Dual-mode playback**: Stereo and 3D spatial audio operators
- **Ultra-low latency**: ~20-60ms typical latency
- **Professional audio**: 48kHz sample rate with hardware acceleration
- **Native 3D audio**: BASS 3D engine with directional cones, Doppler effects
- **Real-time analysis**: FFT spectrum, waveform, and level metering
- **Centralized configuration**: Single source of truth for all audio settings
- **Debug control**: Suppressible logging for cleaner development experience
- **Isolated offline analysis**: Waveform image generation without interfering with playback
- **Stale detection**: Automatic muting of inactive operator streams
- **Export support**: Direct stream reading for video export with audio

---

## Architecture Overview

### Core Components

`
                              AUDIO ENGINE (API)
    ┌───────────────────────────────────────────────────────────────┐
    │  UpdateStereoOperatorPlayback()    Set3DListenerPosition()    │
    │  UpdateSpatialOperatorPlayback()   CompleteFrame()            │
    └───────────────────────────────────────────────────────────────┘
                       │                              │
                       ▼                              ▼
    ┌─────────────────────────────┐    ┌─────────────────────────────┐
    │   AUDIO MIXER MANAGER       │    │      AUDIO CONFIG           │
    │   (BASS Mixer)              │    │      (Configuration)        │
    ├─────────────────────────────┤    ├─────────────────────────────┤
    │  GlobalMixerHandle          │    │  MixerFrequency = 48000 Hz  │
    │  OperatorMixerHandle        │    │  UpdatePeriodMs = 10        │
    │  SoundtrackMixerHandle      │    │  PlaybackBufferLengthMs=100 │
    │  OfflineMixerHandle         │    │  FftBufferSize = 1024       │
    └─────────────────────────────┘    └─────────────────────────────┘
                       │
                       ▼
    ┌─────────────────────────────┐    ┌─────────────────────────────┐
    │  STEREO AUDIO STREAM        │    │  SPATIAL AUDIO STREAM       │
    ├─────────────────────────────┤    ├─────────────────────────────┤
    │  • Volume, Panning, Speed   │    │  • 3D Position/Orientation  │
    │  • Stale/User muting        │    │  • Min/Max distance         │
    │  • Level/Waveform/Spectrum  │    │  • Cones, Doppler           │
    │  • Export metering          │    │  • Stale muting             │
    └─────────────────────────────┘    └─────────────────────────────┘
                       │                              │
                       └──────────────┬───────────────┘
                                      ▼
    ┌─────────────────────────────────────────────────────────────────┐
    │                    OPERATOR GRAPH INTEGRATION                   │
    ├────────────────────────────────┬────────────────────────────────┤
    │     StereoAudioPlayer          │       SpatialAudioPlayer       │
    │     (10 parameters)            │       (21 parameters)          │
    └────────────────────────────────┴────────────────────────────────┘
`

### Mixer Architecture

**Live Playback Path:**
`
Operator Clips ──► OperatorMixer (Decode) ──┐
                                            ├──► GlobalMixer ──► Soundcard
Soundtrack Clips ──► SoundtrackMixer ───────┘
`

**Export Path (GlobalMixer Paused):**
`
Soundtrack Clips ──► Direct ChannelGetData() ──┐
                                               ├──► ResampleAndMix() ──► Video Encoder
OperatorMixer ──► ChannelGetData() ────────────┘
`

**Isolated Analysis (No Output):**
`
AudioFile ──► CreateOfflineAnalysisStream() ──► FFT/Waveform ──► Image Generation
`

### Signal Flow

**Stereo Audio:**
`
AudioFile ──► Decode ──► MixerAddChannel ──► OperatorMixer ──► GlobalMixer ──► Soundcard
                │
                └──► Volume/Panning/Speed ──► StaleMute ──► FFT ──► Level/Waveform/Spectrum
`

**Spatial Audio:**
`
AudioFile ──► Decode (Mono) ──► MixerAddChannel ──► OperatorMixer ──► GlobalMixer ──► Soundcard
                │
                └──► 3D Position ──► Distance/Cone/Doppler ──► Apply3D()
`

**Export:**
`
PrepareRecording() ──► Pause GlobalMixer ──► Remove Soundtracks from Mixer
        │
        ▼
GetFullMixDownBuffer()
        │
        ├──► Seek to position
        ├──► Read Soundtrack data ──► ResampleAndMix() ──┐
        └──► Read OperatorMixer data ────────────────────┴──► MixBuffer ──► FFmpeg
        │
        ▼
EndRecording() ──► Re-add Soundtracks ──► Resume GlobalMixer
`

---

## Audio Operators

### StereoAudioPlayer
**Purpose:** High-quality stereo audio playback with real-time control and analysis.

**Key Parameters:**
- Playback control: Play, Stop (trigger-based, rising edge detection)
- Audio parameters: Volume, Mute, Panning (-1 to 1), Speed (0.1x to 4x), Seek (0-1 normalized)
- Analysis outputs: Audio Level (0-1), Waveform (512 samples), Spectrum (512 bands)
- Debugging: LogDebugInfo

**Use Cases:**
- Background music playback
- Sound effect triggering
- Audio-reactive visuals
- Beat detection integration

**Documentation:** [STEREO_AUDIO_IMPLEMENTATION.md](STEREO_AUDIO_IMPLEMENTATION.md)

### SpatialAudioPlayer
**Purpose:** 3D spatial audio with native BASS 3D engine for immersive soundscapes.

**Key Parameters:**
- All StereoAudioPlayer features plus:
- 3D Position: Source Position (Vector3)
- Distance: Min/Max Distance for attenuation
- Directionality: Source Orientation, Inner/Outer Cone Angles (0-360°), Outer Cone Volume
- Advanced: Audio 3D Mode (Normal/Relative/Off)

**Use Cases:**
- 3D environments and games
- Spatial audio installations
- Directional speakers/emitters
- Doppler effect simulations

**Documentation:** [SPATIAL_AUDIO_IMPLEMENTATION.md](SPATIAL_AUDIO_IMPLEMENTATION.md)

---

## Configuration System

### AudioConfig (Centralized Configuration)

All audio parameters are managed through `Core/Audio/AudioConfig.cs`:

**Mixer Configuration:**
`
MixerFrequency = 48000           // Professional audio quality (Hz)
UpdatePeriodMs = 10              // Low-latency BASS updates
UpdateThreads = 2                // BASS update thread count
PlaybackBufferLengthMs = 100     // Balanced buffering
DeviceBufferLengthMs = 20        // Minimal device latency
`

**FFT and Analysis:**
`
FftBufferSize = 1024             // FFT resolution
BassFftDataFlag = DataFlags.FFT2048  // Returns 1024 values
FrequencyBandCount = 32          // Spectrum bands
WaveformSampleCount = 1024       // Waveform resolution
LowPassCutoffFrequency = 250f    // Low frequency separation (Hz)
HighPassCutoffFrequency = 2000f  // High frequency separation (Hz)
`

**Logging Control:**
`
ShowLogs = false                 // Toggle audio debug/info logs
ShowRenderLogs = false           // Toggle audio rendering logs
LogAudioDebug(message)           // Suppressible debug logging
LogAudioInfo(message)            // Suppressible info logging
LogAudioRenderDebug(message)     // Suppressible render debug logging
LogAudioRenderInfo(message)      // Suppressible render info logging
`

### User Settings Integration

Audio configuration is accessible through the Editor Settings window:

**Location:** `Settings → Profiling and Debugging → Audio System`

**Available Settings:**
- ✅ Show Audio Debug Logs (real-time toggle)
- ✅ Show Audio Render Logs (for export debugging)

**Persistence:** All settings are saved to `UserSettings.json` and restored on startup.

---

## Performance Characteristics

### Latency
- **Typical latency:** ~20-60ms
- **Components:** File I/O (~5ms) + Buffering (~15-55ms) + Device (~5ms)

### CPU Usage
- **Stereo stream:** ~2-3% per active stream
- **Spatial stream:** ~5-10% per active stream (includes 3D calculations)
- **FFT analysis:** ~1-2% overhead (when enabled)

### Memory Usage
- **Stereo stream:** ~200-500KB per stream (depends on file size)
- **Spatial stream:** ~100-250KB per stream (mono requirement = 50% reduction)
- **Analysis buffers:** ~16KB per stream (FFT + waveform)

### Scalability
- **Update frequency:** 60Hz position updates for spatial audio
- **Hardware acceleration:** Utilized where available (3D audio, mixing)

---

## Documentation Index

### Implementation Guides
- **[STEREO_AUDIO_IMPLEMENTATION.md](STEREO_AUDIO_IMPLEMENTATION.md)** - Complete StereoAudioPlayer documentation
- **[SPATIAL_AUDIO_IMPLEMENTATION.md](SPATIAL_AUDIO_IMPLEMENTATION.md)** - Complete SpatialAudioPlayer documentation

### System Documentation
- **[STALE_DETECTION.md](STALE_DETECTION.md)** - Stale detection system documentation

### Development Documentation
- **[CHANGELOG_UPDATE_SUMMARY.md](CHANGELOG_UPDATE_SUMMARY.md)** - Detailed version history

### Configuration Reference
- **`Core/Audio/AudioConfig.cs`** - Source code with inline documentation

---

## Future Enhancement Opportunities

### Environmental Audio
- EAX effects integration (reverb, echo, chorus)
- Room acoustics simulation
- Environmental audio zones

### Advanced 3D Audio
- Custom distance rolloff curves
- Adjustable Doppler factor
- HRTF for headphone spatialization
- Geometry-based occlusion

### Performance Optimizations
- Centralized `Apply3D()` batching
- Stream pooling and recycling
- Async file loading
- Multi-threaded FFT processing

### Current Limitations
1. No EAX environmental effects (BASS supports, not yet exposed)
2. Single Doppler factor (not yet adjustable)
3. No custom distance rolloff curves
4. `Apply3D()` called per stream (could be centralized)

---

## System Architecture Details

### State Management

**Stale Detection Tracking:**
`
private static readonly HashSet<Guid> _operatorsUpdatedThisFrame = new();
private static int _lastStaleCheckFrame = -1;
`

**Stereo Audio State:**
`
private class StereoOperatorAudioState
{
    public StereoOperatorAudioStream? Stream;
    public string CurrentFilePath;
    public bool IsPaused;
    public float PreviousSeek;
    public bool PreviousPlay;      // Rising edge detection
    public bool PreviousStop;      // Rising edge detection
    public bool IsStale;
}
`

**Spatial Audio State:**
`
private class SpatialOperatorAudioState
{
    public SpatialOperatorAudioStream? Stream;
    public string CurrentFilePath;
    public bool IsPaused;
    public float PreviousSeek;
    public bool PreviousPlay;
    public bool PreviousStop;
    public bool IsStale;
}
`

**3D Listener State:**
`
private static Vector3 _listenerPosition = Vector3.Zero;
private static Vector3 _listenerForward = new(0, 0, 1);
private static Vector3 _listenerUp = new(0, 1, 0);
private static bool _3dInitialized = false;
`

### Stream Lifecycle

| Step | Action |
|------|--------|
| 1. INIT | `TryLoadStream()` → Create decode stream → Add to mixer (paused) → Volume=0 |
| 2. PLAY | Rising edge on Play → `Play()` → Unmute → Unpause → IsPlaying=true |
| 3. UPDATE | `UpdateOperatorPlayback()` → Mark as active → Apply Volume/Panning/Speed |
| 4. STALE | `CompleteFrame()` → Check set → If missing → `SetStaleMuted(true)` |
| 5. STOP | Rising edge on Stop → `Stop()` → Pause → Reset position |
| 6. CLEANUP | `UnregisterOperator()` → Dispose stream → Remove from dictionary |

### Stale Detection System

**Purpose:** Automatically mute audio streams when operators stop being evaluated.

**Flow:**
`
Player.Update() ──► UpdateOperatorPlayback() ──► Add operator ID to HashSet
                                                         │
CompleteFrame() ◄────────────────────────────────────────┘
        │
        ├──► For each registered operator:
        │        ├──► If IN set → SetStaleMuted(false) → Restore volume
        │        └──► If NOT in set → SetStaleMuted(true) → Mute (volume=0)
        │
        └──► Clear HashSet for next frame
`

**Stale Muting Implementation:**
`
public void SetStaleMuted(bool muted)
{
    if (_isStaleMuted == muted) return;
    _isStaleMuted = muted;

    if (muted)
        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Volume, 0.0f);
    else if (IsPlaying && !IsPaused && !_isUserMuted)
        Bass.ChannelSetAttribute(StreamHandle, ChannelAttribute.Volume, _currentVolume);
}
`

**Benefits:**
- ✅ Prevents audio from disabled operators
- ✅ Automatic resource management
- ✅ Smooth transitions (streams stay loaded)
- ✅ Maintains playback position
- ✅ Respects user mute settings

### Export System

**Flow:**
| Phase | Actions |
|-------|---------|
| **Prepare** | Pause GlobalMixer → Reset operator streams → Remove soundtracks from mixer |
| **Each Frame** | Update stale states → Seek soundtrack → Read data → Resample → Mix → Update metering |
| **End** | Re-add soundtracks → Restore streams → Resume GlobalMixer |

### Audio Format Support

| Format | Notes |
|--------|-------|
| WAV | PCM, uncompressed |
| MP3 | MPEG Layer 3 |
| OGG | Vorbis |
| FLAC | Via bassflac.dll plugin |

**Requirements:**
- Sample rate: Any (resampled to 48kHz)
- Bit depth: 16-bit or 24-bit recommended
- Channels: Mono or Stereo (Spatial requires Mono)

---

## Best Practices

### Stereo Audio
- Use for music, UI sounds, ambient loops
- Manual panning control for creative effects
- Monitor CPU with many streams

### Spatial Audio
- Use mono sources for best 3D positioning
- Set appropriate min/max distances for scene scale
- Update listener position every frame

### Operator Graph Design
- Leverage stale detection for automatic cleanup
- Trust automatic muting for disabled operators
- Use conditional logic for dynamic playback

### Debugging
- `AudioConfig.ShowAudioLogs = true` for audio debugging
- `AudioConfig.ShowAudioRenderLogs = true` for render debugging
- Check file paths and device configuration

---

## System Requirements

- Windows 10/11 (64-bit)
- .NET 9 Runtime
- Audio output device
- BASS audio library (included)
- bassflac.dll plugin (included)

**Recommended:**
- Dedicated audio hardware
- Low-latency drivers (ASIO, WASAPI)
- 48kHz native sample rate

---

## TODO

- ✅ Switching external input devices
- ✅ AudioReaction
- ✅ Adding a soundtrack to a project
- ✅ Rendering a project with soundtrack duration to mp4
- ✅ PlayVideo with audio (and audio level)
- ✅ Changing audio level in Settings
- Toggling audio mute button
- Exporting a project to the player

