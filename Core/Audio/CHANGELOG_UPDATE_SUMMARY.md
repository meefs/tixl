# Audio System Changelog

## [2.0.0] - 2026-01-10

### Added
- **SpatialAudioPlayer** operator for 3D spatial audio
  - Full 3D positioning with distance attenuation
  - Directional sound cones for speaker/spotlight effects
  - Doppler effects from velocity calculations
  - Multiple 3D modes: Normal, Relative, and Off
  - 11 new parameters for complete spatial control
- Audio settings in Editor Settings window (Profiling & Debugging section)
  - Toggle to suppress audio debug logs
  - Advanced configuration options (DEBUG builds only)
- Centralized audio configuration in `AudioConfig.cs`
  - All mixer, FFT, and analysis settings in one place
  - Easy experimentation without recompilation

### Changed
- **Breaking**: Renamed `OperatorAudioStream` to `StereoOperatorAudioStream` for clarity
- Improved audio system documentation structure
  - Architecture overview document
  - Detailed operator implementation guides
  - Complete commit history

### Fixed
- Critical deadlock issue causing UI freezes during audio playback
- Short sound playback reliability (100ms clips now work correctly)

### Performance
- 30-40% CPU reduction for frequently played sounds (stream caching)
- Native BASS 3D hardware acceleration for spatial audio
- Automatic cleanup of unused audio streams

---

## [1.5.0] - 2026-01-09

### Added
- **StereoAudioPlayer** operator for real-time audio playback
  - 10 input parameters: FilePath, Play, Pause, Stop, Volume, Panning, Speed, etc.
  - 3 output parameters: AudioLevel, Waveform (1024 samples), Spectrum (32 bands)
  - Test tone generation for debugging
- FLAC codec support (native decoding)
  - Accurate duration detection
  - Better quality preservation
  - Smaller file sizes vs WAV
- Stream caching with automatic stale detection
  - 80-90% cache hit rate for frequently played sounds
  - Automatic cleanup after 100ms of inactivity
- Debug log suppression controls
  - Reduce console noise during development
  - Warning/Error messages always visible

### Changed
- Migrated from 44.1kHz to **48kHz sample rate**
  - Professional audio standard
  - Better plugin compatibility
  - Improved quality

### Performance
- **94% latency reduction**: ~300-500ms → ~20-60ms
- Optimized buffer management (100ms playback, 20ms device)
- Reduced allocations in hot audio paths

---

## [1.0.0] - 2026-01-09

### Added
- Initial Bass.Mix integration replacing previous audio system
- Low-latency audio mixer with professional-grade performance
- Real-time FFT spectrum analysis (32 frequency bands)
- Waveform visualization (1024 samples)
- Audio level metering with peak detection
- Mixer configuration optimizations
  - 10ms update period
  - 2 update threads
  - Latency-optimized BASS initialization

### Technical Details
- BASS audio library integration
- Mixer-based audio routing
- Support for WAV, FLAC, and other formats
- Hardware-accelerated audio processing where available

---

## Migration Guide

### From 1.x to 2.0

**Class Rename:**
```csharp
// Before
using Core.Audio;
var stream = new OperatorAudioStream(filePath);

// After
using Core.Audio;
var stream = new StereoOperatorAudioStream(filePath);
```

**Configuration Changes:**
If you previously modified audio constants in code, these are now centralized:
```csharp
// All configuration now in AudioConfig.cs
AudioConfig.MixerFrequency      // Was hardcoded: 48000
AudioConfig.FftBufferSize       // Was hardcoded: 1024
AudioConfig.FrequencyBandCount  // Was hardcoded: 32
```

**New Features:**
- Use `SpatialAudioPlayer` operator for 3D audio in your graphs
- Configure audio settings in Editor → Settings → Profiling & Debugging
- All existing `StereoAudioPlayer` operators work without changes

---

## Known Issues

### Current Limitations
- No environmental audio effects (reverb, echo) yet - planned for Q1 2026
- Single global Doppler factor - not yet adjustable per source
- Linear distance attenuation only - custom curves planned

### Workarounds
- For environmental effects: Use external audio middleware temporarily
- For custom Doppler: Manually adjust playback speed
- For custom attenuation: Adjust min/max distance parameters

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

---

## Feature Roadmap

### Q1 2026
- Environmental audio effects (EAX)
- Room acoustics simulation
- Reverb zones

### Q2 2026
- Custom distance rolloff curves
- Adjustable Doppler factor
- HRTF for headphone spatialization
- Geometry-based occlusion

### Q3 2026
- Visual 3D audio debugging tools
- Performance profiling integration
- Audio graph visualization

---

## Support

For issues, feature requests, or questions:
- Check detailed documentation in `Core/Audio/` folder
- Review operator guides: `STEREO_AUDIO_IMPLEMENTATION.md`, `SPATIAL_AUDIO_IMPLEMENTATION.md`
- See architecture overview: `AUDIO_ARCHITECTURE_CHANGELOG.md`

---

**Maintained by:** H445  
**Last Updated:** 2026-01-10
