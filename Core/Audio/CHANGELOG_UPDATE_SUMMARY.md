# Audio System Changelog

**Maintained by:** H445  
**Last Updated:** 2025-01-10

---

## 2025-01-10 - Stale Detection & 3D Spatial Audio

### Added
- **Automatic Stale Detection System**
  - Frame-based tracking prevents audio from inactive operators
  - **Volume-based muting** (streams continue playing silently in background)
  - Immediate muting/unmuting (no time thresholds)
  - **Respects all UI settings** (volume, mute, panning, speed, seek)
  - Zero user intervention required
  - See `STALE_DETECTION.md` for complete details

- **SpatialAudioPlayer** operator for 3D spatial audio
  - Full 3D positioning with distance attenuation
  - Directional sound cones
  - Doppler effects
  - Multiple 3D modes (Normal, Relative, Off)
  - See `SPATIAL_AUDIO_IMPLEMENTATION.md` for details

- **Audio Settings** in Editor Settings window
  - Toggle to suppress debug logs
  - Advanced configuration options (DEBUG builds)
  - Centralized in `AudioConfig.cs`

### Changed
- **Breaking**: Renamed `OperatorAudioStream` → `StereoOperatorAudioStream`
- Improved documentation structure with focused guides
- **Stale muting mechanism**: Volume-based (not pause-based) for better time synchronization

### Fixed
- Critical UI deadlock during audio playback
- Short sound playback reliability (<100ms clips)
- **User mute priority**: Stale unmute now respects user mute checkbox
- **Timeline sync**: Clarified that operator audio plays at normal speed (not timeline-synced)

### Performance
- Native BASS 3D hardware acceleration
- Optimized stale detection with state change tracking
---

## Previous Updates

### StereoAudioPlayer & FLAC Support
- Real-time audio playback operator with 10 inputs, 3 outputs
- FLAC codec support (native decoding)
- Stream caching with automatic cleanup
- 48kHz sample rate (professional standard)

### Initial Bass.Mix Integration
- Low-latency audio mixer
- Real-time FFT spectrum analysis (32 bands)
- Waveform visualization (1024 samples)
- Audio level metering
- Hardware-accelerated processing

---

## Known Limitations
- No environmental effects (reverb, echo) yet
- Linear distance attenuation only (custom curves planned)
- Single global Doppler factor

---

## System Requirements
- Windows 10/11 (64-bit)
- .NET 9 Runtime
- BASS audio library (included)

**Recommended:**
- Low-latency audio drivers (ASIO, WASAPI)
- 48kHz native sample rate support

---

## Roadmap
- Environmental audio effects (EAX, reverb zones)
- Custom distance rolloff curves
- HRTF for headphone spatialization
- Visual 3D audio debugging tools
- Performance profiling integration

---

## Documentation
- `AUDIO_ARCHITECTURE.md` - System overview
- `STALE_DETECTION.md` - Automatic muting system
- `STEREO_AUDIO_IMPLEMENTATION.md` - StereoAudioPlayer guide
- `SPATIAL_AUDIO_IMPLEMENTATION.md` - SpatialAudioPlayer guide
