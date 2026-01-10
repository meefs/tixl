# 3D Spatial Audio Implementation for SpatialAudioPlayer

## Overview
Implemented full 3D spatial audio support for the `SpatialAudioPlayer` operator with distance-based attenuation and positional panning.

## Components Created/Modified

### 1. SpatialOperatorAudioStream.cs (NEW)
**Location:** `Core/Audio/SpatialOperatorAudioStream.cs`

**Purpose:** Dedicated audio stream class for 3D spatial audio playback with mono sources

**Key Features:**
- Mono audio source loading (required for effective 3D positioning)
- Distance-based volume attenuation using inverse distance law
- Automatic 3D panning based on listener-relative position
- Min/Max distance parameters for attenuation control
- Smooth position updates with minimal performance impact

**Distance Attenuation Model:**
```
- Distance < MinDistance: Full volume (attenuation = 1.0)
- MinDistance < Distance < MaxDistance: Linear falloff
- Distance >= MaxDistance: Silent (attenuation = 0.0)
```

**Panning Model:**
- Based on listener's local coordinate system using orientation vectors
- **Right Vector** = Cross product of `ListenerForward` × `ListenerUp`
- **Pan Calculation** = Dot product of (normalized sound direction) · (right vector)
- Positive pan = Right speaker (sound to listener's right)
- Negative pan = Left speaker (sound to listener's left)
- Range: -1.0 to +1.0
- **Orientation-Aware**: Panning adapts when listener rotates

### 2. AudioEngine.cs (MODIFIED)
**Location:** `Core/Audio/AudioEngine.cs`

**New Methods:**

#### Spatial Audio Playback
- `UpdateSpatialOperatorPlayback()`: Main update method for spatial audio streams
- `PauseSpatialOperator()`: Pause spatial audio playback
- `ResumeSpatialOperator()`: Resume spatial audio playback
- `IsSpatialOperatorStreamPlaying()`: Check if spatial stream is playing
- `IsSpatialOperatorPaused()`: Check if spatial stream is paused
- `GetSpatialOperatorLevel()`: Get current audio level
- `GetSpatialOperatorWaveform()`: Get waveform data
- `GetSpatialOperatorSpectrum()`: Get spectrum data

#### 3D Listener Management
- `Set3DListenerPosition()`: Set the 3D audio listener position and orientation
- `Get3DListenerPosition()`: Get current listener position

**New State Tracking:**
- `_spatialOperatorAudioStates`: Dictionary tracking spatial audio stream states per operator
- `_listenerPosition`: Current 3D listener position
- `_listenerForward`: Listener forward direction vector
- `_listenerUp`: Listener up direction vector

### 3. SpatialAudioPlayer.cs (MODIFIED)
**Location:** `Operators/Lib/io/audio/SpatialAudioPlayer.cs`

**Updated Features:**
- Uses `UpdateSpatialOperatorPlayback()` instead of regular playback
- Passes 3D position, minDistance, and maxDistance parameters
- Validates and clamps distance parameters
- Enhanced debug output showing position and distance info
- Updates listener position in AudioEngine each frame

**Input Parameters:**
- `SourcePosition` (Vector3): 3D position of the audio source
- `ListenerPosition` (Vector3): 3D position of the listener (e.g., camera or player position)
- `ListenerForward` (Vector3): Forward direction vector of the listener (normalized)
- `ListenerUp` (Vector3): Up direction vector of the listener (normalized)
- `MinDistance` (float): Distance where attenuation begins
- `MaxDistance` (float): Distance where sound becomes inaudible

**Note on Listener Orientation:**
- If `ListenerForward` is zero or near-zero, defaults to `(0, 0, 1)` (facing +Z)
- If `ListenerUp` is zero or near-zero, defaults to `(0, 1, 0)` (up is +Y)
- Vectors are automatically normalized by the system

## Technical Implementation Details

### Distance Calculation
```csharp
var distance = Vector3.Distance(listenerPos, sourcePos);
```

### Attenuation Formula
```csharp
if (distance > minDistance) {
    if (distance >= maxDistance) {
        attenuation = 0.0f;
    } else {
        attenuation = 1.0f - ((distance - minDistance) / (maxDistance - minDistance));
    }
}
```

### Panning Calculation
```csharp
// Get listener's right vector (cross product of forward and up)
var listenerRight = Vector3.Normalize(Vector3.Cross(listenerForward, listenerUp));

// Project sound direction onto listener's right vector
var toSound = Vector3.Normalize(sourcePos - listenerPos);
float panValue = Vector3.Dot(toSound, listenerRight);
panValue = Math.Clamp(panValue, -1.0f, 1.0f);
```

**Panning Behavior:**
- Sound to the listener's **right**: Positive pan value (→ right speaker)
- Sound to the listener's **left**: Negative pan value (→ left speaker)
- Sound directly in front/behind: Near-zero pan value (→ center)
- Takes into account which way the listener is facing

## Performance Optimizations

1. **Cached Channel Info**: Channel information is cached to avoid expensive API calls
2. **Stale Detection**: Automatic muting of inactive streams to save CPU
3. **Update Throttling**: Position logging limited to once per second
4. **Efficient Distance Calculations**: Using Vector3.Distance for optimized math

## Usage Example

```csharp
// In your operator graph:
SpatialAudioPlayer
{
    AudioFile = "path/to/audio.wav"
    PlayAudio = true
    SourcePosition = (x: 10, y: 0, z: 5)        // Sound source: 10 units right, 5 units forward
    ListenerPosition = (x: 0, y: 0, z: 0)       // Listener at origin (e.g., camera position)
    ListenerForward = (x: 0, y: 0, z: 1)        // Facing forward (+Z direction)
    ListenerUp = (x: 0, y: 1, z: 0)             // Up is +Y direction
    MinDistance = 1.0   // Full volume within 1 unit
    MaxDistance = 50.0  // Silent beyond 50 units
    Volume = 1.0
}

// Listener position and orientation are set per-operator via inputs
// This allows different spatial audio sources to have different listener references if needed
// The AudioEngine.Set3DListenerPosition() is called automatically during Update()
```

## Advanced Usage: Dynamic Listener Position and Orientation

```csharp
// Connect camera position and orientation to listener inputs
SpatialAudioPlayer
{
    AudioFile = "footsteps.wav"
    PlayAudio = true
    SourcePosition = enemyPosition              // Enemy's position
    ListenerPosition = cameraPosition           // Camera/player position (updated each frame)
    ListenerForward = cameraForwardVector       // Camera's forward direction
    ListenerUp = cameraUpVector                 // Camera's up direction
    MinDistance = 2.0
    MaxDistance = 30.0
    Volume = 0.8
}

// The spatial audio will automatically:
// 1. Calculate distance between listener and source
// 2. Apply distance-based volume attenuation
// 3. Calculate stereo panning based on listener's local coordinate system:
//    - Sounds to the right of where you're facing → right speaker
//    - Sounds to the left of where you're facing → left speaker
//    - Works correctly even when listener rotates!
```

## Example: Rotating Listener
```csharp
// Listener facing North (forward = 0, 0, 1)
// Sound at (5, 0, 0) → Pans RIGHT (positive pan)

// Listener rotates 90° to face East (forward = 1, 0, 0)
// Same sound at (5, 0, 0) → Now pans FORWARD (near-zero pan)
// This is because the sound is now ahead of the listener, not to the right
```

## Differences from StereoAudioPlayer

| Feature | StereoAudioPlayer | SpatialAudioPlayer |
|---------|-------------------|-------------------|
| Audio Format | Stereo (2-channel) | Any format (mono recommended for best 3D effect) |
| Panning Control | Manual pan slider (-1 to +1) | Automatic based on 3D position |
| Distance Attenuation | None | Automatic (min/max distance) |
| Volume Control | Direct volume only | Volume × Distance attenuation |
| Position Inputs | None | SourcePosition + ListenerPosition (Vector3) |
| Use Case | Music, ambient sounds | Positioned sound effects, 3D audio, spatial soundscapes |

## Key Features Summary

### Inputs
- **SourcePosition** (Vector3): Where the sound is coming from in 3D space
- **ListenerPosition** (Vector3): Where the listener (camera/player) is located
- **ListenerForward** (Vector3): Direction the listener is facing (normalized automatically)
- **ListenerUp** (Vector3): Listener's up direction (normalized automatically)
- **MinDistance** (float): Distance within which sound is at full volume
- **MaxDistance** (float): Distance beyond which sound is silent
- **Volume** (float): Base volume multiplier (0-1)
- **Mute** (bool): Mute the sound
- **Speed** (float): Playback speed (0.1-4.0)
- **Seek** (float): Playback position (0-1 normalized)

### Automatic Calculations
- **Distance**: `Vector3.Distance(ListenerPosition, SourcePosition)`
- **Attenuation**: Linear falloff between MinDistance and MaxDistance
- **Panning**: Based on dot product of sound direction with listener's right vector
  - Right vector = `Cross(ListenerForward, ListenerUp)`
  - Takes into account which way the listener is facing

## Limitations & Future Enhancements

### Current Limitations:
1. **Simple Attenuation Model**: Linear falloff (could be logarithmic for more realistic behavior)
2. **Basic Panning**: Only left-right panning (no elevation or behind-listener positioning)
3. **No Doppler Effect**: Moving sound sources don't change pitch
4. **No Environmental Effects**: No reverb or occlusion based on geometry

### Potential Enhancements:
1. **Advanced Attenuation Models**:
   - Logarithmic distance falloff
   - Exponential rolloff options
   - Custom attenuation curves

2. **Full 3D Panning**:
   - HRTF (Head-Related Transfer Function) for elevation
   - Behind-listener sound localization
   - Surround sound support (5.1, 7.1)

3. **Environmental Audio**:
   - Reverb zones
   - Occlusion/obstruction
   - Material-based sound absorption

4. **Performance Features**:
   - Sound culling (auto-stop distant sounds)
   - Priority system for sound limit management
   - LOD (Level of Detail) for distant sounds

## Testing Recommendations

1. **Distance Test**:
   - Place sound at increasing distances
   - Verify smooth attenuation
   - Check min/max distance boundaries

2. **Panning Test**:
   - Move sound left-to-right
   - Verify correct channel distribution
   - Check center position (0 pan)

3. **Performance Test**:
   - Multiple spatial sounds simultaneously
   - Rapid position changes
   - Monitor CPU usage

4. **Edge Cases**:
   - Sound at listener position (distance = 0)
   - Sound beyond max distance
   - Rapid on/off toggling

## Compatibility

- **BASS Version**: Compatible with current ManagedBass implementation
- **.NET Version**: .NET 9
- **C# Version**: 13.0
- **Audio Formats**: All formats supported by BASS (WAV, MP3, OGG, FLAC, etc.)

## Build Status
✅ **Build Successful** - All compilation errors resolved

## Known Issues & Fixes

### Issue: "No3D" Error on Stream Creation
**Error Message:** `[SpatialAudio] Error loading audio stream 'filename.wav': No3D`

**Cause:** Initial implementation attempted to use `BassFlags.Bass3D` flag which requires BASS to be initialized with 3D support (`DeviceInitFlags.Device3D`).

**Fix:** Removed `BassFlags.Bass3D` from stream creation. The implementation now uses software-based 3D positioning through:
- Distance-based volume attenuation
- Automatic stereo panning based on X-axis position
- No dependency on BASS's native 3D audio system

**Benefits of Software-Based Approach:**
- ✅ Works with any BASS initialization mode
- ✅ More predictable and portable across platforms
- ✅ Full control over attenuation curves and panning algorithms
- ✅ No additional BASS configuration required

### Enhancement: Orientation-Aware Panning (v2)
**Feature:** Panning now takes into account listener orientation (forward and up vectors)

**How it works:**
1. Calculates listener's local coordinate system (right vector = forward × up)
2. Projects sound direction onto the right vector
3. Panning adapts correctly when listener rotates

**Before:** Panning was based solely on X-axis (world coordinates)
- Sound at (10, 0, 0) always panned right, regardless of listener orientation

**After:** Panning is based on listener's local right direction
- Sound pans right/left relative to where the listener is facing
- If listener rotates 180°, a sound that was "to the right" becomes "to the left"

**Benefits:**
- ✅ More realistic 3D audio experience
- ✅ Works correctly with rotating cameras/players
- ✅ Matches user expectations for spatial audio in games/VR
