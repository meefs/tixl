# Audio System Architecture

**Version:** 2.0  
**Last Updated:** 2026-01-24
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
- **Dual-mode playback**: Stereo and 3D spatial audio operators
- **Device-native sample rate**: Automatically matches output device sample rate via WASAPI query
- **Low-latency configuration**: Configurable update periods and buffer sizes
- **Native 3D audio**: BASS 3D engine with directional cones, Doppler effects, velocity-based positioning
- **Real-time analysis**: FFT spectrum, waveform, and level metering for both live and export
- **Centralized configuration**: Single source of truth via `AudioConfig`
- **Debug control**: Suppressible logging for cleaner development experience
- **Isolated offline analysis**: Waveform image generation without interfering with playback
- **Stale detection**: Automatic muting of inactive operator streams per-frame
- **Export support**: Direct stream reading for video export with audio (soundtrack + operator mixing)
- **Unified codebase**: Common base class (`OperatorAudioStreamBase`) eliminates code duplication
- **FLAC support**: Native BASS FLAC plugin for high-quality audio files
- **External audio mode support**: Handles external device audio sources during export (operators only)

---

## Architecture Overview

```
                              AUDIO ENGINE (API)
    ┌───────────────────────────────────────────────────────────────┐
    │  UpdateStereoOperatorPlayback()    Set3DListenerPosition()    │
    │  UpdateSpatialOperatorPlayback()   CompleteFrame()            │
    │  UseSoundtrackClip()               ReloadSoundtrackClip()     │
    │  PauseOperator/ResumeOperator      GetOperatorLevel           │
    │  UnregisterOperator()              SetGlobalVolume/Mute       │
    │  OnAudioDeviceChanged()            SetSoundtrackMute()        │
    │  TryGetStereoOperatorStream()      TryGetSpatialOperatorStream│
    │  GetClipChannelCount()             GetClipSampleRate()        │
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
    │  GetSoundtrackMixerLevel()  │    │  LogAudioDebug/Info/Render  │
    │  SetGlobalVolume/Mute()     │    │  ShowAudioLogs toggle       │
    │  SetOperatorMute()          │    │  ShowAudioRenderLogs toggle │
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
    │  • SetStaleMuted(muted)        • TryLoadStreamCore (static)     │
    └─────────────────────────────────────────────────────────────────┘
                      │
          ┌───────────┴───────────┐
          ▼                       ▼
    ┌─────────────────┐    ┌─────────────────┐
    │  STEREO STREAM  │    │  SPATIAL STREAM │
    ├─────────────────┤    ├─────────────────┤
    │  • SetPanning() │    │  • 3D Position  │
    │  • TryLoadStream│    │  • Orientation  │
    │                 │    │  • Velocity     │
    │                 │    │  • Cone/Doppler │
    │                 │    │  • Apply3D()    │
    │                 │    │  • Set3DMode()  │
    │                 │    │  • Initialize3D │
    └─────────────────┘    └─────────────────┘
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
- **Device Management**: `OnAudioDeviceChanged()`, `SetGlobalVolume()`, `SetGlobalMute()`
- **Export Support**: `ResetAllOperatorStreamsForExport()`, `RestoreOperatorAudioStreams()`

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
- **Compile-time**: Buffer sizes, FFT configuration, frequency band counts
- **Logging helpers**: `LogAudioDebug()`, `LogAudioInfo()`, `LogAudioRenderDebug()`, `LogAudioRenderInfo()`

---

## Class Hierarchy

```
OperatorAudioStreamBase (abstract)
├── Properties: Duration, StreamHandle, MixerStreamHandle, IsPaused, IsPlaying, FilePath
├── Protected: DefaultPlaybackFrequency, CachedChannels, CachedFrequency, IsStaleMuted
├── Methods: Play, Pause, Resume, Stop, SetVolume, SetSpeed, Seek
├── Metering: GetLevel, UpdateFromBuffer, ClearExportMetering
├── Export: PrepareForExport, RestartAfterExport, RenderAudio, GetCurrentPosition
│
├── StereoOperatorAudioStream
│   ├── TryLoadStream(filePath, mixerHandle) - Factory method
│   └── SetPanning(float) - Pan audio left (-1) to right (+1)
│
└── SpatialOperatorAudioStream
    ├── TryLoadStream(filePath, mixerHandle) - Factory method (loads as mono)
    ├── Initialize3DAudio() - Setup 3D attributes
    ├── Update3DPosition(Vector3, float, float) - Position + min/max distance
    ├── Set3DOrientation(Vector3) - Directional facing
    ├── Set3DCone(float, float, float) - Inner/outer angle + volume
    └── Set3DMode(Mode3D) - Normal/Relative/Off

AudioPlayerUtils (static utility)
└── ComputeInstanceGuid(IEnumerable<Guid>) - Stable operator identification via FNV-1a hash

OperatorAudioUtils (static utility)
├── FillAndResample(...) - Buffer filling with resampling/channel conversion
└── LinearResample(...) - Simple linear resampler and up/down-mixer

AudioEngine (static)
├── Soundtrack: SoundtrackClipStreams, UseSoundtrackClip, ReloadSoundtrackClip
├── Operators: _stereoOperatorStates, _spatialOperatorStates, Update*OperatorPlayback
├── 3D Listener: _listenerPosition, _listenerForward, _listenerUp, Set3DListenerPosition
├── Stale Detection: _operatorsUpdatedThisFrame, CheckAndMuteStaleOperators
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
Operator Clips ──► OperatorMixer (Decode) ──────┐
                   [MixerChanBuffer]            │
                                                ├──► GlobalMixer ──► Soundcard
Soundtrack Clips ──► SoundtrackMixer (Decode) ──┘
                     [MixerChanBuffer]
```

### Export Path (GlobalMixer Paused)
```
Soundtrack Clips ──► Direct ChannelGetData() ──┐
                     (removed from mixer)      │
                                               ├──► ResampleAndMix() ──► Video Encoder
OperatorMixer ──► ChannelGetData() ────────────┘
                  (stays in mixer)
```

### Isolated Analysis (No Output)
```
AudioFile ──► CreateOfflineAnalysisStream() ──► FFT/Waveform ──► Image Generation
              (Decode + Prescan flags)          (no soundcard)
```

---

## Signal Flow

### Stereo Audio
```
AudioFile ──► Bass.CreateStream (Decode|Float|AsyncFile)
          │
          ├──► BassMix.MixerAddChannel (MixerChanBuffer|MixerChanPause)
          │                │
          │                └──► OperatorMixer ──► GlobalMixer ──► Soundcard
          │
          └──► SetVolume/SetPanning/SetSpeed ──► BassMix.ChannelGetLevel ──► Metering
```

### Spatial Audio
```
AudioFile ──► Bass.CreateStream (Decode|Float|AsyncFile|Mono)
          │
          ├──► BassMix.MixerAddChannel (MixerChanBuffer|MixerChanPause)
          │                │
          │                └──► OperatorMixer ──► GlobalMixer ──► Soundcard
          │
          └──► 3D Position ──► Distance/Cone/Velocity ──► Bass.Apply3D()
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
**Purpose:** High-quality stereo audio playback with real-time control and analysis.

**Key Parameters:**
- Playback control: Play, Stop (trigger-based, rising edge detection)
- Audio parameters: Volume (0-1), Mute, Panning (-1 to 1), Speed (0.1x to 4x), Seek (0-1 normalized)
- Analysis outputs: IsPlaying, IsPaused, Level (0-1)

**Implementation Details:**
- Uses `AudioPlayerUtils.ComputeInstanceGuid()` for stable operator identification
- Delegates all audio logic to `AudioEngine.UpdateStereoOperatorPlayback()`
- Supports `RenderAudio()` for export functionality
- Finalizer unregisters operator from AudioEngine via `UnregisterOperator()`

**Use Cases:**
- Background music playback
- Sound effect triggering
- Audio-reactive visuals
- Beat detection integration

### SpatialAudioPlayer
**Purpose:** 3D spatial audio with native BASS 3D engine for immersive soundscapes.

**Key Parameters:**
- All StereoAudioPlayer features (except Panning) plus:
- 3D Position: SourcePosition, ListenerPosition, ListenerForward, ListenerUp (Vector3)
- Distance: MinDistance, MaxDistance for attenuation
- Directionality: SourceOrientation, InnerConeAngle, OuterConeAngle (0-360°), OuterConeVolume
- Advanced: Audio3DMode (Normal/Relative/Off)

**Implementation Details:**
- Loads audio as mono for optimal 3D positioning
- Listener orientation auto-normalized if invalid
- 3D position updated every frame via `AudioEngine.Set3DListenerPosition()`
- Velocity computed from position delta (assumes ~60fps)
- Supports `RenderAudio()` for export functionality

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
        ├──► ResetAllOperatorStreamsForExport()
        └──► Remove Soundtrack streams from SoundtrackMixer (for direct reading)
        │
        ▼
GetFullMixDownBuffer() [per frame]
        │
        ├──► UpdateStaleStatesForExport()
        ├──► MixSoundtrackClip() ──► Seek + Read + ResampleAndMix()
        ├──► MixOperatorAudio() ──► Read from OperatorMixer
        ├──► UpdateOperatorMetering()
        ├──► PopulateFromExportBuffer() ──► WaveForm buffers
        └──► ComputeFftFromBuffer() ──► FFT buffers
        │
        ▼
EndRecording()
        │
        ├──► Re-add Soundtrack streams to SoundtrackMixer
        ├──► RestoreState() (export state)
        └──► RestoreOperatorAudioStreams()
```

### External Audio Mode
When `AudioSource` is set to `ExternalDevice` during export:
- Soundtrack mixing is skipped entirely
- Only operator audio is included in export
- Warning is logged to inform user
- Waveform buffers are cleared (external audio can't be monitored)

---

## Documentation Index

### Core Files

| File                              | Purpose                                          |
|-----------------------------------|--------------------------------------------------|
| `AudioEngine.cs`                  | Central API for operator and soundtrack playback |
| `OperatorAudioStreamBase.cs`      | Common stream functionality (abstract base)      |
| `StereoOperatorAudioStream.cs`    | Stereo-specific stream with panning              |
| `SpatialOperatorAudioStream.cs`   | 3D spatial stream with positioning               |
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
- **[STALE_DETECTION.md](STALE_DETECTION.md)** - Stale detection system
- **[TODO.md](TODO.md)** - Technical review and next steps 

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
- Centralized `Apply3D()` batching
- Stream pooling and recycling
- Async file loading
- Multithreaded FFT processing

### Current Limitations
1. No EAX environmental effects (BASS supports, not yet exposed)
2. Single Doppler factor (not yet adjustable)
3. No custom distance rolloff curves
4. `Apply3D()` called per stream (could be centralized)

---

# Immediate TODO:
- Finish implementing SpatialAudioPlayer
- Re-think the seek logic / probably should only seek on play
- Add the sample accurate adsr envelope to Stereo and Spatial Audio Players
- Re-visit the Waveform/Spectrum outputs (may not work correctly at the moment)
- Add unit tests for AudioEngine methods
- Implement remaining technical review items

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
