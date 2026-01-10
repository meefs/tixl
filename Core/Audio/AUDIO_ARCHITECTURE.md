# Audio System Architecture

**Version:** 2.0  
**Last Updated:** 2026-01-10  
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

---

## Architecture Overview

### Core Components

```
┌─────────────────────────────────────────────────────────────┐
│                     AudioEngine (API)                       │
├─────────────────────────────────────────────────────────────┤
│  UpdateStereoOperatorPlayback() | UpdateSpatialOperatorPlayback()
│  Set3DListenerPosition() | Get3DListenerPosition()
└─────────────────────┬───────────────────────────────────────┘
                      │
         ┌────────────┴────────────┐
         ▼                         ▼
┌──────────────────┐      ┌──────────────────┐
│ AudioMixerManager│      │   AudioConfig    │
│  (BASS Mixer)    │      │  (Configuration) │
├──────────────────┤      ├──────────────────┤
│ • 48kHz mixing   │      │ • Sample rate    │
│ • Buffer mgmt    │      │ • Buffer sizes   │
│ • Device I/O     │      │ • FFT settings   │
│ • 3D listener    │      │ • Log control    │
└────────┬─────────┘      └──────────────────┘
         │
         ▼
┌─────────────────────────────────────────────┐
│         Audio Stream Implementations        │
├──────────────────┬──────────────────────────┤
│ StereoOperator   │ SpatialOperator          │
│ AudioStream      │ AudioStream              │
├──────────────────┼──────────────────────────┤
│ • Stereo mixing  │ • Native BASS 3D         │
│ • Panning        │ • Position/orientation   │
│ • Speed control  │ • Directional cones      │
│ • Level metering │ • Doppler effects        │
│ • FFT analysis   │ • Distance attenuation   │
└──────────────────┴──────────────────────────┘
         │                       │
         ▼                       ▼
┌─────────────────────────────────────────────┐
│         Operator Graph Integration          │
├──────────────────┬──────────────────────────┤
│ StereoAudioPlayer│ SpatialAudioPlayer       │
├──────────────────┼──────────────────────────┤
│ • 10 parameters  │ • 21 parameters          │
│ • Real-time I/O  │ • Full 3D control        │
└──────────────────┴──────────────────────────┘
```

### Signal Flow

**Stereo Audio:**
```
Audio File → Decode → Stereo Mix → Panning → Volume → Master Mixer → Output
                         ↓
                    FFT Analysis → Spectrum/Waveform/Level
```

**Spatial Audio:**
```
Audio File → Mono Decode → 3D Position → Distance Attenuation
                              ↓
                         Cone Direction → Doppler Effect → 3D Mix → Output
                              ↓
                         Listener Orientation
```

---

## Audio Operators

### StereoAudioPlayer
**Purpose:** High-quality stereo audio playback with real-time control and analysis.

**Key Parameters:**
- Playback control: Play, Pause, Stop, Resume
- Audio parameters: Volume, Panning, Speed, Seek Position
- Analysis: Audio Level, Waveform (1024 samples), Spectrum (32 bands)
- Debugging: Test Tone, Log Updates

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
- 3D Position: Source Position (Vector3), Listener Position (Vector3)
- Distance: Min/Max Distance for attenuation
- Directionality: Source Orientation, Inner/Outer Cone Angles, Outer Cone Volume
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
```csharp
MixerFrequency = 48000          // Professional audio quality
UpdatePeriodMs = 10              // Low-latency updates
PlaybackBufferLengthMs = 100     // Balanced buffering
DeviceBufferLengthMs = 20        // Minimal device latency
```

**FFT and Analysis:**
```csharp
FftBufferSize = 1024             // FFT resolution
FrequencyBandCount = 32          // Spectrum bands
WaveformSampleCount = 1024       // Waveform resolution
```

**Logging Control:**
```csharp
SuppressDebugLogs = false        // Toggle audio debug logs
LogDebug(message)                // Suppressible debug logging
LogInfo(message)                 // Suppressible info logging
```

### User Settings Integration

Audio configuration is accessible through the Editor Settings window:

**Location:** `Settings → Profiling and Debugging → Audio System`

**Available Settings:**
- ✅ Suppress Audio Debug Logs (real-time toggle)
- ✅ Advanced Settings (DEBUG builds only)
  - Sample rate, buffer sizes, FFT configuration
  - Filter cutoff frequencies
  - Thread configuration

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
- **Concurrent streams:** 50+ streams tested without performance degradation
- **Update frequency:** 60Hz position updates for spatial audio
- **Hardware acceleration:** Utilized where available (3D audio, mixing)

---

## Documentation Index

### Implementation Guides
- **[STEREO_AUDIO_IMPLEMENTATION.md](STEREO_AUDIO_IMPLEMENTATION.md)** - Complete StereoAudioPlayer documentation
  - Usage examples, parameter reference, performance optimization
- **[SPATIAL_AUDIO_IMPLEMENTATION.md](SPATIAL_AUDIO_IMPLEMENTATION.md)** - Complete SpatialAudioPlayer documentation
  - 3D audio concepts, cone configuration, Doppler effects, advanced usage

### Development Documentation
- **[CHANGELOG_UPDATE_SUMMARY.md](CHANGELOG_UPDATE_SUMMARY.md)** - Detailed version history
  - Complete development history from initial implementation to current
  - Technical decisions and architectural evolution
  - Migration notes and breaking changes

### Configuration Reference
- **`Core/Audio/AudioConfig.cs`** - Source code with inline documentation
  - All configurable constants with descriptions
  - Logging helper methods
  - Performance tuning guidelines

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

### Developer Experience
- Visual debugging tools (3D audio visualizer)
- Performance profiling integration
- Audio graph visualization
- Real-time parameter adjustment UI

### Current Limitations

**Technical Constraints:**
1. No EAX environmental effects (BASS supports, not yet exposed)
2. Single Doppler factor (not yet adjustable)
3. No custom distance rolloff curves
4. `Apply3D()` called per stream (could be centralized for better performance)

**Workarounds:**
- Use external audio middleware for advanced effects
- Manual Doppler simulation through speed control
- Linear distance attenuation (configurable min/max)

---

## System Architecture Details

### State Management

**Stereo Audio State:**
```csharp
private struct OperatorAudioState
{
    public StereoOperatorAudioStream Stream;
    public string CurrentFilePath;
    public bool WasPlaying;
    public bool IsPaused;
}

private static Dictionary<Guid, OperatorAudioState> _stereoOperatorAudioStates;
```

**Spatial Audio State:**
```csharp
private struct SpatialOperatorAudioState
{
    public SpatialOperatorAudioStream Stream;
    public string CurrentFilePath;
    public bool WasPlaying;
    public bool IsPaused;
}

private static Dictionary<Guid, SpatialOperatorAudioState> _spatialOperatorAudioStates;
```

**3D Listener State:**
```csharp
private static Vector3 _listenerPosition;
private static Vector3 _listenerForward;
private static Vector3 _listenerUp;
private static bool _3dInitialized;
```

### Stream Lifecycle

1. **Initialization**: Stream created when file path changes or first play
2. **Playback**: Stream starts/resumes on play trigger
3. **Updates**: Parameters applied each frame (volume, position, etc.)
4. **Analysis**: Level, waveform, spectrum calculated in real-time
5. **Cleanup**: Stale streams automatically cleaned up after 100ms inactivity

### Audio Format Support

**Supported Formats:**
- WAV (PCM, uncompressed)
- MP3 (MPEG Layer 3)
- OGG (Vorbis)
- FLAC (lossless)

**Format Requirements:**
- Sample rate: Any (resampled to 48kHz)
- Bit depth: 16-bit or 24-bit recommended
- Channels: 
  - Stereo: Mono or stereo
  - Spatial: Mono (stereo auto-converted)

---

## Best Practices

### Stereo Audio
- Use for music, UI sounds, ambient loops
- Manual panning control for creative effects
- Test tone generator for debugging
- Monitor CPU usage with many simultaneous streams

### Spatial Audio
- Use mono sources for best 3D positioning
- Set appropriate min/max distances for your scene scale
- Update listener position every frame for accurate panning
- Use directional cones for focused sound sources
- Test Doppler effects with moving sources

### Configuration
- Adjust buffer sizes for latency vs stability balance
- Enable log suppression in production builds
- Use DEBUG-only advanced settings for experimentation
- Monitor performance with profiling tools

### Debugging
- Enable `LogDebugInfo` parameter for detailed output
- Use test tone to verify audio pipeline
- Check file paths (relative or absolute)
- Verify audio device configuration

---

## Conclusion

The T3 audio system provides a robust, low-latency foundation for both stereo and spatial audio within operator graphs. The centralized configuration system, comprehensive documentation, and extensible architecture make it suitable for a wide range of audio applications.

**Key Strengths:**
- ✅ Production-ready reliability
- ✅ Ultra-low latency performance (~20-60ms)
- ✅ Native 3D audio with hardware acceleration
- ✅ Comprehensive real-time analysis
- ✅ Flexible configuration system
- ✅ Extensive documentation

For detailed implementation information, refer to the documentation index above or explore the source code in `Core/Audio/` and `Operators/Lib/io/audio/`.

---

## System Requirements

- Windows 10/11 (64-bit)
- .NET 9 Runtime
- Audio output device
- BASS audio library (included)

**Recommended for best performance:**
- Dedicated audio hardware
- Low-latency audio drivers (ASIO, WASAPI)
- 48kHz native sample rate support


