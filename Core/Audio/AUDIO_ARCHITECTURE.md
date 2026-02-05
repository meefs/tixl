# Audio System Architecture

**Version:** 2.2  
**Last Updated:** 2026-01-30
**Status:** Production Ready

---

## Table of Contents
1. [Introduction](#introduction)
2. [Architecture Overview](#architecture-overview)
3. [Core Components](#core-components)
4. [Class Hierarchy](#class-hierarchy)
5. [Mixer Architecture](#mixer-architecture)
6. [Signal Flow](#signal-flow)
7. [Audio Operators](#audio-operators)
8. [Configuration System](#configuration-system)
9. [Export and Rendering](#export-and-rendering)
10. [Documentation Index](#documentation-index)
11. [Future Enhancement Opportunities](#future-enhancement-opportunities)

---

## Introduction

The TiXL audio system is a high-performance, low-latency audio engine built on ManagedBass, supporting stereo and 3D spatial audio playback within operator graphs.

### Key Features
- **Dual-mode playback**: Stereo (via mixer) and 3D spatial audio (direct to BASS) operators
- **Device-native sample rate**: Automatically matches output device sample rate via WASAPI query
- **Low-latency configuration**: Configurable update periods and buffer sizes
- **Native 3D audio**: BASS 3D engine with directional cones, Doppler effects, velocity-based positioning (hardware-accelerated via direct BASS output)
- **Configurable 3D factors**: Distance, rolloff, and Doppler factors via `AudioConfig`
- **Real-time analysis**: FFT spectrum, waveform, and level metering for both live and export
- **Centralized configuration**: Single source of truth via `AudioConfig`
- **Debug control**: Suppressible logging for cleaner development experience
- **Isolated offline analysis**: Waveform image generation without interfering with playback
- **Stale detection**: Automatic muting of inactive operator streams per-frame
- **Export support**: BASS-native export mixer with automatic resampling for video export (soundtrack + operator mixing)
- **Unified codebase**: Common base class (`OperatorAudioStreamBase`) for stereo streams; standalone class for spatial
- **FLAC support**: Native BASS FLAC plugin for high-quality audio files
- **External audio mode support**: Handles external device audio sources during export (operators only)
- **Batched 3D updates**: `Apply3D()` called once per frame for optimal performance
- **ADSR envelope support**: Built-in envelope generator for amplitude modulation on `AudioPlayer`

---

## Architecture Overview

```
                              AUDIO ENGINE (API)
    ┌───────────────────────────────────────────────────────────────┐
    │  UpdateStereoOperatorPlayback()    Set3DListenerPosition()    │
    │  UpdateSpatialOperatorPlayback()   CompleteFrame()            │
    │  UseSoundtrackClip()               ReloadSoundtrackClip()     │
    │  PauseOperator/ResumeOperator      GetOperatorLevel           │
    │  PauseSpatialOperator/Resume       GetSpatialOperatorLevel    │
    │  IsSpatialOperatorStreamPlaying()  IsSpatialOperatorPaused()  │
    │  UnregisterOperator()              SetGlobalVolume/Mute       │
    │  OnAudioDeviceChanged()            SetSoundtrackMute()        │
    │  TryGetStereoOperatorStream()      TryGetSpatialOperatorStream│
    │  GetClipChannelCount()             GetClipSampleRate()        │
    │  GetAllStereoOperatorStates()      GetAllSpatialOperatorStates│
    │  Get3DListenerPosition/Forward/Up                             │
    └───────────────────────────────────────────────────────────────┘
                       │                              │
                       ▼                              ▼
    ┌─────────────────────────────┐    ┌─────────────────────────────┐
    │   AUDIO MIXER MANAGER       │    │      AUDIO CONFIG           │
    │   (BASS Mixer)              │    │      (Configuration)        │
    ├─────────────────────────────┤    ├─────────────────────────────┤
    │  GlobalMixerHandle          │    │  MixerFrequency (from dev)  │
    │  OperatorMixerHandle        │    │  UpdatePeriodMs = 10        │
    │  SoundtrackMixerHandle      │    │  PlaybackBufferLengthMs=100 │
    │  OfflineMixerHandle         │    │  DeviceBufferLengthMs = 20  │
    │  CreateOfflineAnalysisStream│    │  FftBufferSize = 1024       │
    │  GetGlobalMixerLevel()      │    │  FrequencyBandCount = 32    │
    │  GetOperatorMixerLevel()    │    │  WaveformSampleCount = 1024 │
    │  GetSoundtrackMixerLevel()  │    │  DistanceFactor = 1.0       │
    │  SetGlobalVolume/Mute()     │    │  RolloffFactor = 1.0        │
    │  SetOperatorMute()          │    │  DopplerFactor = 1.0        │
    │                             │    │  LogAudioDebug/Info/Render  │
    │                             │    │  ShowAudioLogs toggle       │
    │                             │    │  ShowAudioRenderLogs toggle │
    └─────────────────────────────┘    └─────────────────────────────┘
                       │
                       ▼
    ┌─────────────────────────────────────────────────────────────────┐
    │              OPERATOR AUDIO STREAM BASE (Abstract)              │
    ├─────────────────────────────────────────────────────────────────┤
    │  • Play/Pause/Resume/Stop      • Volume/Speed/Seek              │
    │  • Stale/User muting           • GetLevel (metering)            │
    │  • Export metering             • RenderAudio for export         │
    │  • PrepareForExport            • RestartAfterExport             │
    │  • UpdateFromBuffer            • ClearExportMetering            │
    │  • GetCurrentPosition          • Dispose                        │
    │  • SetStale(stale)             • TryLoadStreamCore (static)     │
    └─────────────────────────────────────────────────────────────────┘
                      │
          ┌───────────┴───────────┐
          ▼                       ▼
    ┌─────────────────┐    ┌─────────────────────────┐
    │  STEREO STREAM  │    │     SPATIAL STREAM      │
    ├─────────────────┤    ├─────────────────────────┤
    │  • SetPanning() │    │  • 3D Position          │
    │  • TryLoadStream│    │  • Orientation/Velocity │
    │  • Uses Mixer   │    │  • Cone/Doppler         │
    │                 │    │  • Apply3D()            │
    │                 │    │  • Set3DMode()          │
    │                 │    │  • Initialize3D         │
    │                 │    │  • DIRECT to BASS (no   │
    │                 │    │    mixer for HW 3D)     │
    └─────────────────┘    └─────────────────────────┘
                       │
                       ▼
    ┌─────────────────────────────────────────────────────────────────┐
    │                    OPERATOR GRAPH INTEGRATION                   │
    ├────────────────────────────────┬────────────────────────────────┤
    │     AudioPlayer                │       SpatialAudioPlayer       │
    │     (Uses AudioPlayerUtils)    │       (Uses AudioPlayerUtils)  │
    └────────────────────────────────┴────────────────────────────────┘
```

---

## Core Components

### AudioEngine
The central API for all audio operations. Key responsibilities:
- **Soundtrack Management**: `UseSoundtrackClip()`, `ReloadSoundtrackClip()`, `CompleteFrame()`
- **Operator Playback**: `UpdateStereoOperatorPlayback()`, `UpdateSpatialOperatorPlayback()`
- **3D Listener**: `Set3DListenerPosition()`, `Get3DListenerPosition/Forward/Up()`
- **State Queries**: `IsOperatorStreamPlaying()`, `IsOperatorPaused()`, `GetOperatorLevel()`
- **Spatial State Queries**: `IsSpatialOperatorStreamPlaying()`, `IsSpatialOperatorPaused()`, `GetSpatialOperatorLevel()`
- **Device Management**: `OnAudioDeviceChanged()`, `SetGlobalVolume()`, `SetGlobalMute()`
- **Export Support**: `ResetAllOperatorStreamsForExport()`, `RestoreOperatorAudioStreams()`, `UpdateStaleStatesForExport()`
- **Export Metering**: `GetAllStereoOperatorStates()`, `GetAllSpatialOperatorStates()`

### AudioMixerManager
Manages the BASS mixer hierarchy. Key features:
- **Automatic device sample rate detection** via WASAPI query before BASS init
- **Low-latency configuration** applied before initialization
- **FLAC plugin loading** for native FLAC support
- **Four mixer handles** for different purposes (see Mixer Architecture)
- **Level metering** via `GetGlobalMixerLevel()`, `GetOperatorMixerLevel()`, `GetSoundtrackMixerLevel()`
- **Offline analysis streams** via `CreateOfflineAnalysisStream()` / `FreeOfflineAnalysisStream()`

### AudioConfig
Centralized configuration with compile-time and runtime settings:
- **Runtime**: `MixerFrequency` (set from device), `ShowAudioLogs`, `ShowAudioRenderLogs`
- **3D Audio**: `DistanceFactor`, `RolloffFactor`, `DopplerFactor` (configurable at runtime)
- **Compile-time**: Buffer sizes, FFT configuration, frequency band counts
- **Logging helpers**: `LogAudioDebug()`, `LogAudioInfo()`, `LogAudioRenderDebug()`, `LogAudioRenderInfo()`

---

## Class Hierarchy

```
OperatorAudioStreamBase (abstract)
├── Properties: Duration, StreamHandle, MixerStreamHandle, IsPaused, IsPlaying, FilePath
├── Protected: DefaultPlaybackFrequency, CachedChannels, CachedFrequency, IsStaleStopped
├── Methods: Play, Pause, Resume, Stop, SetVolume, SetSpeed, Seek
├── Metering: GetLevel, UpdateFromBuffer, ClearExportMetering
├── Export: PrepareForExport, RestartAfterExport, RenderAudio, GetCurrentPosition
│
└── StereoOperatorAudioStream (extends base, uses mixer)
    ├── TryLoadStream(filePath, mixerHandle) - Factory method
    └── SetPanning(float) - Pan audio left (-1) to right (+1)

SpatialOperatorAudioStream (standalone class - does NOT inherit from base)
├── Properties: Duration, StreamHandle, FilePath, IsPaused, IsPlaying, IsStaleStopped
├── Methods: Play, Pause, Resume, Stop, SetVolume, SetSpeed, Seek
├── 3D Methods:
│   ├── Initialize3DAudio() - Setup initial 3D attributes
│   ├── Update3DPosition(Vector3, float, float) - Position + min/max distance
│   ├── Set3DOrientation(Vector3) - Directional facing
│   ├── Set3DCone(float, float, float) - Inner/outer angle + volume
│   └── Set3DMode(Mode3D) - Normal/Relative/Off
├── TryLoadStream(filePath, mixerHandle) - Factory (mixerHandle ignored, plays direct)
├── Metering: GetLevel, UpdateFromBuffer (export)
├── Export State: _exportDecodeStreamHandle, _isExportMode, _exportPlaybackPosition
├── Distance Attenuation: _distanceAttenuation, _isBeyondMaxDistance (linear rolloff)
└── Note: Plays DIRECTLY to BASS output for hardware 3D processing

AudioPlayerUtils (static utility)
└── ComputeInstanceGuid(IEnumerable<Guid>) - Stable operator identification via FNV-1a hash

OperatorAudioUtils (static utility)
├── FillAndResample(...) - Buffer filling with resampling/channel conversion
└── LinearResample(...) - Simple linear resampler and up/down-mixer

AudioEngine (static)
├── Soundtrack: SoundtrackClipStreams, UseSoundtrackClip, ReloadSoundtrackClip
├── Operators: _stereoOperatorStates, _spatialOperatorStates, Update*OperatorPlayback
├── Internal State Classes:
│   ├── OperatorAudioState<T> - Stream, CurrentFilePath, IsPaused, PreviousSeek/Play/Stop, IsStale, LastUpdatedFrameId
│   └── SpatialOperatorState - Same structure but non-generic for spatial streams
├── 3D Listener: _listenerPosition, _listenerForward, _listenerUp, Set3DListenerPosition
├── 3D Batching: Mark3DApplyNeeded(), Apply3DChanges() (called once per frame)
├── Stale Detection: _audioFrameToken (monotonic), LastUpdatedFrameId per operator, StopStaleOperators
│   └── See STALE_DETECTION.md for detailed documentation
├── Export: ResetAllOperatorStreamsForExport, RestoreOperatorAudioStreams, UpdateStaleStatesForExport
└── Device: OnAudioDeviceChanged, SetGlobalVolume, SetGlobalMute, SetOperatorMute
```

---

## Mixer Architecture

The mixer architecture uses a hierarchical structure with separate paths for different audio sources:

### Mixer Handles

| Handle | Flags | Purpose |
|--------|-------|---------|
| **GlobalMixerHandle** | `Float \| MixerNonStop` | Master output to soundcard |
| **OperatorMixerHandle** | `MixerNonStop \| Decode \| Float` | Operator audio decode submixer |
| **SoundtrackMixerHandle** | `MixerNonStop \| Decode \| Float` | Soundtrack decode submixer |
| **OfflineMixerHandle** | `Decode \| Float` | Isolated decode for analysis (no output) |

### Live Playback Path
```
Stereo Operator Clips ──► OperatorMixer (Decode) ──────┐
                          [MixerChanBuffer]            │
                                                       ├──► GlobalMixer ──► Soundcard
Soundtrack Clips ──► SoundtrackMixer (Decode) ─────────┘
                     [MixerChanBuffer]

Spatial Operator Clips ──► BASS Direct (3D Flags) ──────► Soundcard (hardware 3D)
                           [Mono + Bass3D + Float]
```

> **Note:** Spatial streams bypass the mixer hierarchy entirely to enable hardware-accelerated 3D audio 
> processing with native BASS 3D engine support. They play directly to the BASS output device.

### Export Path (GlobalMixer Paused)
```
Soundtrack Clips ──► Export Mixer ────────────────┐
                     (BassMix resampling)         │
                     (removed from SoundtrackMixer│
                                                  ├──► MixBuffer ──► Video Encoder
OperatorMixer ──► ChannelGetData() ───────────────┤
                  (stereo decode)                 │
                                                  │
Spatial Streams ──► RenderAudio() ────────────────┘
                    (decode stream + manual 3D)
```

### Isolated Analysis (No Output)
```
AudioFile ──► CreateOfflineAnalysisStream() ──► FFT/Waveform ──► Image Generation
              (Decode + Prescan flags)          (no soundcard)
```

### Stereo vs Spatial Output Flow

| Aspect | Stereo Streams | Spatial Streams |
|--------|----------------|-----------------|
| **Output Path** | Through OperatorMixer → GlobalMixer → Soundcard | Direct to BASS → Soundcard |
| **Stream Flags** | `Decode \| Float \| AsyncFile` | `Float \| Mono \| Bass3D \| AsyncFile` |
| **Mixer Channel** | Added via `BassMix.MixerAddChannel` | Not added to any mixer |
| **Playback Method** | `BassMix.ChannelPlay` | `Bass.ChannelPlay` |
| **3D Processing** | None (2D stereo) | Hardware-accelerated via BASS 3D engine |
| **Level Metering** | `BassMix.ChannelGetLevel` | `Bass.ChannelGetLevel` |
| **Why?** | Mixer provides flexible routing, volume control | 3D requires native BASS for HW acceleration |

> **Design Decision:** Spatial audio bypasses the mixer to leverage BASS's hardware-accelerated 3D 
> positioning. Routing through the mixer would break the native 3D audio chain, as the mixer outputs 
> standard stereo which cannot be repositioned in 3D space afterwards.

---

## Signal Flow

### Stereo Audio (Uses Mixer)
```
AudioFile ──► Bass.CreateStream (Decode|Float|AsyncFile)
          │
          ├──► BassMix.MixerAddChannel (MixerChanBuffer|MixerChanPause)
          │                │
          │                └──► OperatorMixer ──► GlobalMixer ──► Soundcard
          │
          └──► SetVolume/SetPanning/SetSpeed ──► BassMix.ChannelGetLevel ──► Metering
```

### Spatial Audio (Direct to BASS - Hardware 3D)
```
AudioFile ──► Bass.CreateStream (Float|Mono|Bass3D|AsyncFile)
          │
          │   [NO MIXER - Direct to BASS Output]
          │
          ├──► Bass.ChannelPlay() ──────────────────────────────► Soundcard (HW 3D)
          │
          ├──► 3D Position ──► Bass.ChannelSet3DPosition() ──────┐
          │                                                       │
          ├──► 3D Attributes ──► Bass.ChannelSet3DAttributes() ──┼──► Bass.Apply3D()
          │    (Mode, Distance, Cone)                             │
          │                                                       │
          └──► Velocity ──► Doppler Effect ──────────────────────┘
```

### FFT and Waveform Analysis (Live)
```
GlobalMixer ──► Bass.ChannelGetData(FFT2048) ──► FftGainBuffer ──► ProcessUpdate()
            │                                                          │
            │                                    ┌─────────────────────┘
            │                                    ▼
            │                              FrequencyBands[32]
            │                              FrequencyBandPeaks[32]
            │                              FrequencyBandAttacks[32]
            │
            └──► Bass.ChannelGetData(samples) ──► InterleavenSampleBuffer
                                                          │
                                                          ▼
                                                    WaveformLeftBuffer[1024]
                                                    WaveformRightBuffer[1024]
                                                    WaveformLow/Mid/HighBuffer[1024]
```

---

## Audio Operators

### AudioPlayer
**Purpose:** High-quality stereo audio playback with real-time control, ADSR envelope, and analysis.

**Key Parameters:**
- Playback control: Play, Stop, Pause (trigger-based, rising edge detection)
- Audio parameters: Volume (0-1), Mute, Panning (-1 to 1), Speed (0.1x to 4x), Seek (0-1 normalized)
- **ADSR Envelope**: TriggerMode (OneShot/Gate/Loop), Duration, UseEnvelope toggle
- **Envelope Vector4**: X=Attack, Y=Decay, Z=Sustain (level), W=Release
- Analysis outputs: IsPlaying, GetLevel (0-1)

**Implementation Details:**
- Uses `AudioPlayerUtils.ComputeInstanceGuid()` for stable operator identification
- Delegates all audio logic to `AudioEngine.UpdateStereoOperatorPlayback()`
- Supports `RenderAudio()` for export functionality
- **ADSR via `AdsrCalculator`**: Envelope modulates volume in real-time when `UseEnvelope` is enabled
- Finalizer unregisters operator from AudioEngine via `UnregisterOperator()`

**Use Cases:**
- Background music playback
- Sound effect triggering with ADSR envelope shaping
- Audio-reactive visuals
- Beat detection integration

### SpatialAudioPlayer
**Purpose:** 3D spatial audio with native BASS 3D engine for immersive soundscapes.

**Key Parameters:**
- Playback: Play, Stop, Pause (trigger-based, rising edge detection)
- Audio: Volume (0-1), Mute, Speed (0.1x to 4x), Seek (0-1 normalized)
- **3D Source**: SourcePosition (Vector3), SourceRotation (Vector3 Euler degrees)
- **3D Listener**: ListenerPosition (Vector3), ListenerRotation (Vector3 Euler degrees)
- **Distance**: MinDistance, MaxDistance for attenuation
- **Directionality**: InnerConeAngle (0-360°), OuterConeAngle (0-360°), OuterConeVolume (0-1)
- **Advanced**: Audio3DMode (Normal/Relative/Off), GizmoVisibility

**Outputs:**
- `Result`: Command output for operator chaining
- `GizmoOutput`: Internal gizmo rendering output
- `IsPlaying`, `IsPaused`: Playback state queries
- `GetLevel`: Current audio amplitude (0-1)

**Implementation Details:**
- **Implements `ITransformable`** for gizmo manipulation (TranslationInput → SourcePosition, RotationInput → SourceRotation)
- **Loads audio as mono** with `BassFlags.Bass3D | BassFlags.Mono | BassFlags.Float` for optimal 3D positioning
- **Plays directly to BASS output** (NOT through OperatorMixer) for hardware-accelerated 3D processing
- Does not use `BassMix.MixerAddChannel` - the `mixerHandle` parameter is ignored in `TryLoadStream`
- Listener rotation converted from Euler angles to forward/up vectors via rotation matrix
- 3D position updated every frame via `AudioEngine.Set3DListenerPosition()`
- Velocity computed from position delta (assumes ~60fps) for Doppler effects
- **Linear distance attenuation**: Custom rolloff from minDistance to maxDistance (BASS uses inverse distance)
- Uses `AudioEngine.Mark3DApplyNeeded()` to batch 3D changes per frame
- `Bass.Apply3D()` called once per frame in `CompleteFrame()` for performance
- Supports `RenderAudio()` for export functionality (uses separate decode stream)

**Use Cases:**
- 3D environments and games
- Spatial audio installations
- Directional speakers/emitters
- Doppler effect simulations

---

## Configuration System

### AudioConfig (Centralized Configuration)

All audio parameters are managed through `Core/Audio/AudioConfig.cs`:

**Mixer Configuration:**
```csharp
MixerFrequency       // Determined from device's current sample rate (runtime)
UpdatePeriodMs = 10              // Low-latency BASS updates
UpdateThreads = 2                // BASS update thread count
PlaybackBufferLengthMs = 100     // Balanced buffering
DeviceBufferLengthMs = 20        // Minimal device latency
```

**3D Audio Configuration:**
```csharp
DistanceFactor = 1.0f            // Units per meter (1.0 = 1 unit = 1 meter)
RolloffFactor = 1.0f             // Distance attenuation (0 = none, 1 = real-world)
DopplerFactor = 1.0f             // Doppler effect strength (0 = none, 1 = real-world)
```

**FFT and Analysis:**
```csharp
FftBufferSize = 1024             // FFT resolution
BassFftDataFlag = DataFlags.FFT2048  // Returns 1024 values
FrequencyBandCount = 32          // Spectrum bands
WaveformSampleCount = 1024       // Waveform resolution
LowPassCutoffFrequency = 250f    // Low frequency separation (Hz)
HighPassCutoffFrequency = 2000f  // High frequency separation (Hz)
```

**Logging Control:**
```csharp
ShowAudioLogs = false            // Toggle audio debug/info logs
ShowAudioRenderLogs = false      // Toggle audio rendering logs
LogAudioDebug(message)           // Suppressible debug logging
LogAudioInfo(message)            // Suppressible info logging
LogAudioRenderDebug(message)     // Suppressible render debug logging
LogAudioRenderInfo(message)      // Suppressible render info logging
Initialize(showAudioLogs, showAudioRenderLogs)  // Editor initialization
```

### User Settings Integration

Audio configuration is accessible through the Editor Settings window:

**Location:** `Settings → Profiling and Debugging → Audio System`

**Available Settings:**
- ✅ Show Audio Debug Logs (real-time toggle)
- ✅ Show Audio Render Logs (for export debugging)

**Persistence:** All settings are saved to `UserSettings.json` and restored on startup.

---

## Export and Rendering

### Export Flow

```
PrepareRecording()
        │
        ├──► Pause GlobalMixer
        ├──► Clear AudioExportSourceRegistry
        ├──► Reset WaveFormProcessing export buffer
        ├──► ResetAllOperatorStreamsForExport() (both stereo + spatial)
        ├──► Create Export Mixer (BassMix, Decode + Float + MixerNonStop)
        └──► Remove Soundtrack streams from SoundtrackMixer, add to Export Mixer
        │
        ▼
GetFullMixDownBuffer() [per frame]
        │
        ├──► UpdateStaleStatesForExport() (both stereo + spatial)
        ├──► MixSoundtracksFromExportMixer() ──► Position + Volume + ChannelGetData
        ├──► MixOperatorAudio() ──► Read from OperatorMixer (stereo only)
        ├──► MixSpatialOperatorAudio() ──► Read from decode streams (spatial)
        ├──► UpdateOperatorMetering() (both stereo + spatial)
        ├──► PopulateFromExportBuffer() ──► WaveForm buffers
        └──► ComputeFftFromBuffer() ──► FFT buffers
        │
        ▼
EndRecording()
        │
        ├──► Remove Soundtrack streams from Export Mixer
        ├──► Re-add Soundtrack streams to SoundtrackMixer
        ├──► Free Export Mixer
        ├──► RestoreState() (export state)
        └──► RestoreOperatorAudioStreams()
```

### Export Mixer Design
The export system uses a dedicated BASS mixer (`_exportMixerHandle`) that handles resampling automatically:
- Created with `BassFlags.Decode | BassFlags.Float | BassFlags.MixerNonStop`
- Soundtrack streams are added with `BassFlags.MixerChanNoRampin | BassFlags.MixerChanPause`
- BASS handles resampling from each clip's native frequency to the mixer frequency

### External Audio Mode
When `AudioSource` is set to `ExternalDevice` during export:
- Soundtrack mixing is skipped entirely
- Only operator audio is included in export
- Warning is logged to inform user
- Waveform buffers are cleared (external audio can't be monitored)

### Spatial Audio Export Notes
During export, spatial audio streams require special handling:
- Spatial streams use a **separate decode stream** (`_exportDecodeStreamHandle`) for reading audio data
- The hardware 3D processing is **not applied** during export (raw mono audio is exported)
- 3D positioning effects are only present during live playback
- Exported spatial audio is mixed as mono, then converted to stereo for the final mixdown

> **Important:** Spatial audio in exported videos will NOT include 3D positioning effects. (Doppler)
> The exported audio is the raw source audio mixed to stereo with simulated spatial processing.

---

## Documentation Index

### Core Files

| File                              | Purpose                                          |
|-----------------------------------|--------------------------------------------------|
| `AudioEngine.cs`                  | Central API for operator and soundtrack playback |
| `OperatorAudioStreamBase.cs`      | Common stream functionality (abstract base)      |
| `StereoOperatorAudioStream.cs`    | Stereo-specific stream with panning (uses mixer) |
| `SpatialOperatorAudioStream.cs`   | 3D spatial stream (standalone, direct to BASS)   |
| `AudioRendering.cs`               | Export/recording functionality                   |
| `AudioMixerManager.cs`            | BASS mixer setup and level metering              |
| `AudioConfig.cs`                  | Centralized configuration                        |
| `AudioAnalysis.cs`                | FFT processing and frequency bands               |
| `AudioAnalysisResult.cs`          | Analysis result data structures                  |
| `OperatorAudioUtils.cs`           | Buffer filling and resampling utilities          |
| `WaveFormProcessing.cs`           | Waveform buffer management and filtering         |
| `SoundtrackClipDefinition.cs`     | Soundtrack clip data structures                  |
| `SoundtrackClipStream.cs`         | Soundtrack stream playback                       |
| `AudioExportSourceRegistry.cs`    | Registry for export audio sources                |
| `IAudioExportSource.cs`           | Interface for exportable audio sources           |
| `WasapiAudioInput.cs`             | External WASAPI audio device input               |
| `BeatSynchronizer.cs`             | Beat detection and timing                        |
| `BeatTimingDetails.cs`            | Beat timing data structures                      |
| `AdsrCalculator.cs`               | ADSR envelope calculation utility                |

### Operator Files

| File              | Purpose                         |
|-------------------|---------------------------------|
| `AudioPlayer.cs`  | Stereo playback operator        |
| `SpatialAudioPlayer.cs` | 3D spatial playback operator    |
| `AudioPlayerUtils.cs` | Shared utilities (instance GUID)|
| `AudioToneGenerator.cs` | Tone generation operator        |

### Guides
- **[TODO.md](TODO.md)** - Technical review, stale detection details, and next steps 

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
- Geometry-based occlusion (maybe)

### Performance Optimizations
- ✅ **Centralized `Apply3D()` batching** - Implemented via `Mark3DApplyNeeded()` and single call in `CompleteFrame()`
- Stream pooling and recycling
- Async file loading (partially via `BassFlags.AsyncFile`)
- Multithreaded FFT processing

### Current Limitations
1. No EAX environmental effects (BASS supports, not yet exposed)
2. Single Doppler factor (not yet adjustable per-stream)
3. No custom distance rolloff curves
4. Spatial audio not included in mixer-level metering (plays directly to BASS)
5. Export of spatial audio uses separate decode stream (not hardware 3D processed)

---

# Immediate TODO:
- ✅ Finish implementing SpatialAudioPlayer (ITransformable, gizmo, rotation inputs)
- ✅ Add the sample accurate ADSR envelope to AudioPlayer via AdsrCalculator
- Re-think the seek logic?
- Add unit tests for AudioEngine methods
- Implement remaining technical review items
- Add ADSR envelope support to SpatialAudioPlayer

# Diff Summary

Diff summary for branch `Bass-AudioImplementation` vs `upstream/main`

Added

- `Core/Audio/AUDIO_ARCHITECTURE.md` — new architecture/design doc for the audio subsystem.
- `Core/Audio/AdsrCalculator.cs` — ADSR envelope calculation utility.
- `Core/Audio/AudioAnalysisResult.cs` — analysis result data structures.
- `Core/Audio/AudioConfig.cs` — centralized audio configuration and logging toggles.
- `Core/Audio/AudioExportSourceRegistry.cs` — registry for export/record audio sources.
- `Core/Audio/AudioMixerManager.cs` — BASS mixer initialization/management and helpers.
- `Core/Audio/IAudioExportSource.cs` — interface for exportable audio sources.
- `Core/Audio/OperatorAudioStreamBase.cs` — abstract base for operator audio streams.
- `Core/Audio/OperatorAudioUtils.cs` — helper utilities for operator streams.
- `Core/Audio/STALE_DETECTION.md` — doc for stale stream detection.
- `Core/Audio/SpatialOperatorAudioStream.cs` — spatial/3D operator stream implementation.
- `Core/Audio/StereoOperatorAudioStream.cs` — stereo operator stream implementation.
- `Core/Audio/TODO.md` — audio-specific TODO / technical review list.
- `Core/Audio/BeatTimingDetails.cs` — beat timing data structures.
- `Dependencies/bassflac.dll` — native FLAC plugin binary (new dependency).
- `Dependencies/bassmix.dll` — native BASS mixer plugin (new dependency).
- `Editor/Gui/InputUi/CombinedInputs/AdsrEnvelopeInputUi.cs` — UI input for ADSR envelope.
- `Editor/Gui/OpUis/UIs/AdsrEnvelopeUi.cs` — ADSR editor UI control.
- `Editor/Gui/Windows/SettingsWindow.AudioPanel.cs` — audio panel for settings window.
- `Operators/Lib/io/audio/AudioPlayerUtils.cs` — shared operator audio utilities.
- `Operators/Lib/io/audio/AudioToneGenerator.cs` (+ `.t3`/`.t3ui`) — tone generator operator and UI.
- `Operators/Lib/io/audio/SpatialAudioPlayer.cs` (+ `.t3`/`.t3ui`) — spatial audio operator and UI metadata.
- `Operators/Lib/io/audio/AudioPlayer.cs` (+ `.t3`/`.t3ui`) — stereo audio operator and UI metadata.
- `Operators/Lib/numbers/anim/AdsrEnvelope.cs` — ADSR data structure/operator type.
- `Operators/examples/lib/io/audio/AudioPlaybackExample.*` — example operator for audio playback.
- `Resources/audio/HH_03.wav`, `Resources/audio/KICK_09.wav`, `Resources/audio/SNARE_01.wav`, `Resources/audio/h445-loop1.wav` — added sample audio resources.

Modified

- `Core/Audio/AudioAnalysis.cs` — FFT/waveform handling updates and buffer ownership changes.
- `Core/Audio/AudioEngine.cs` — central audio API changes for playback/update/export integration.
- `Core/Audio/AudioRendering.cs` — export/mixdown improvements and buffer reuse notes.
- `Core/Audio/BeatSynchronizer.cs` — beat detection / timing adjustments.
- `Core/Audio/WasapiAudioInput.cs` — Wasapi input adjustments.
- `Core/Audio/WaveFormProcessing.cs` — waveform processing tweaks.
- `Core/Core.csproj` — project file updated (Core).
- `Core/IO/ProjectSettings.cs` — project settings changes.
- `Core/Operator/PlaybackSettings.cs` — operator playback settings modified.
- `Core/Operator/Symbol.Child.cs` — symbol child related updates.
- `Editor/Gui/Audio/AudioImageFactory.cs` — audio image factory updates.
- `Editor/Gui/Audio/AudioImageGenerator.cs` — audio image generation tweaks.
- `Editor/Gui/InputUi/VectorInputs/Vector4InputUi.cs` — vector4 input UI changes.
- `Editor/Gui/Interaction/Timing/PlaybackUtils.cs` — playback timing helpers updated.
- `Editor/Gui/OpUis/OpUi.cs` — operator UI adjustments.
- `Editor/Gui/UiHelpers/UserSettings.cs` — user settings persistence/UX changes.
- `Editor/Gui/Windows/RenderExport/RenderAudioInfo.cs` — render audio info updates.
- `Editor/Gui/Windows/RenderExport/RenderProcess.cs` — render/export process changes.
- `Editor/Gui/Windows/RenderExport/RenderTiming.cs` — render timing adjustments.
- `Editor/Gui/Windows/SettingsWindow.cs` — settings window updated to include audio panel.
- `Editor/Gui/Windows/TimeLine/PlaybackSettingsPopup.cs` — timeline playback settings tweaks.
- `Editor/Gui/Windows/TimeLine/TimeControls.cs` — timeline controls updated.
- `Editor/Program.cs` — editor startup changes to include audio initialization.
- `Operators/Lib/io/video/PlayAudioClip.cs` — video operator audio clip glue changes.
- `Operators/Lib/io/video/PlayVideo.cs` — play video operator adjusted for audio changes.
- `Operators/Lib/io/video/PlayVideoClip.cs` — video clip operator updates.
- `Operators/Lib/Lib.csproj` — operators lib project updated.
- `Player/Player.csproj`, `Player/Program.RenderLoop.cs`, `Player/Program.cs` — player project and playback loop adjusted for audio changes.

Renamed

- `Core/Audio/AudioClipDefinition.cs` → `Core/Audio/SoundtrackClipDefinition.cs` — renamed soundtrack clip definition.
- `Core/Audio/AudioClipStream.cs` → `Core/Audio/SoundtrackClipStream.cs` — renamed soundtrack clip stream.
