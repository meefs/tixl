using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using ManagedBass;
using ManagedBass.Mix;
using T3.Core.Audio;
using T3.Core.Logging;

namespace Lib.io.audio
{
    /// <summary>
    /// Generates test tones procedurally in real-time via the operator mixer.
    /// No external files are created - audio is synthesized on-the-fly.
    /// Play input is a trigger (0→1 pulse) that starts playback for the specified duration.
    /// </summary>
    [Guid("7c8f3a2e-9d4b-4e1f-8a5c-6b2d9f7e4c3a")]
    internal sealed class TestToneGenerator : Instance<TestToneGenerator>, IAudioExportSource
    {
        [Input(Guid = "3e9a7f2c-4d8b-4c1f-9e5a-2b7d6f8c4a5e")]
        public readonly InputSlot<bool> Trigger = new();

        [Input(Guid = "8f4a2e9c-7d3b-4e1f-8c5a-9b2d6f7c3a4e")]
        public readonly InputSlot<float> Frequency = new();

        [Input(Guid = "2c9f4e7a-3d8b-4a1f-9e5c-6b2d7f8c4a3e")]
        public readonly InputSlot<float> Duration = new();

        [Input(Guid = "c0645e37-db4e-4658-9d65-96478851f6f6")]
        public readonly InputSlot<float> Volume = new();

        [Input(Guid = "1a3f4b7c-12d3-4a5b-9c7d-8e1f2a3b4c5d")]
        public readonly InputSlot<bool> Mute = new();

        [Input(Guid = "53d1622e-b1d5-4b1c-acd0-ebceb7064043")]
        public readonly InputSlot<float> Panning = new();

        [Input(Guid = "5a7e9f2c-8d4b-4c1f-9a5e-3b2d6f7c8a4e", MappedType = typeof(WaveformTypes))]
        public readonly InputSlot<int> WaveformType = new();

        [Output(Guid = "b7e2c1a4-5d3f-4e8a-9c2f-1e4b7a6c3d8f")]
        public readonly Slot<Command> Result = new();

        [Output(Guid = "960aa0a3-89b4-4eff-8b52-36ff6965cf8f")]
        public readonly Slot<bool> IsPlaying = new();

        [Output(Guid = "b09d215a-bcf0-479a-a649-56f9c698ecb1")]
        public readonly Slot<float> GetLevel = new();

        [Output(Guid = "8f4e2d1a-3b7c-4d89-9e12-7a5b8c9d0e1f")]
        public readonly Slot<List<float>> GetWaveform = new();

        [Output(Guid = "7f8e9d2a-4b5c-3e89-8f12-6a5b9c8d0e2f")]
        public readonly Slot<List<float>> GetSpectrum = new();

        private Guid _operatorId;
        private ProceduralToneStream? _toneStream;
        private bool _previousTrigger;
        private bool _isRegistered;

        public TestToneGenerator()
        {
            Result.UpdateAction += Update;
            IsPlaying.UpdateAction += Update;
            GetLevel.UpdateAction += Update;
            GetWaveform.UpdateAction += Update;
            GetSpectrum.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            if (_operatorId == Guid.Empty)
            {
                _operatorId = AudioPlayerUtils.ComputeInstanceGuid(InstancePath);
                AudioConfig.LogAudioDebug($"[TestToneGenerator] Initialized: {_operatorId}");
            }

            var trigger = Trigger.GetValue(context);
            var frequency = Frequency.GetValue(context);
            var duration = Duration.GetValue(context);
            var volume = Volume.GetValue(context);
            var mute = Mute.GetValue(context);
            var panning = Panning.GetValue(context);
            var waveformType = WaveformType.GetValue(context);

            // Apply defaults
            if (frequency <= 0) frequency = 440f;
            if (duration <= 0) duration = float.MaxValue; // 0 or negative = infinite

            // Ensure stream is created
            EnsureToneStream();

            if (_toneStream == null)
            {
                IsPlaying.Value = false;
                GetLevel.Value = 0;
                GetWaveform.Value = null;
                GetSpectrum.Value = null;
                return;
            }

            // Update parameters (can be changed while playing)
            _toneStream.Frequency = frequency;
            _toneStream.WaveformType = (WaveformTypes)waveformType;

            // Trigger on rising edge only (0→1)
            // Falling edge (1→0) does NOT stop playback - this is a trigger, not a gate
            var triggered = trigger && !_previousTrigger;
            _previousTrigger = trigger;

            if (triggered)
            {
                // Set duration before playing
                _toneStream.DurationSeconds = duration;
                _toneStream.Play();
                AudioConfig.LogAudioDebug($"[TestToneGenerator] ▶ Triggered @ {frequency}Hz for {(duration < float.MaxValue ? $"{duration}s" : "∞")}");
            }

            // Check if duration elapsed (stream handles this internally)
            if (_toneStream.HasFinished)
            {
                _toneStream.Stop();
                AudioConfig.LogAudioDebug($"[TestToneGenerator] ■ Duration elapsed");
            }

            // Update volume/mute/panning while playing
            if (_toneStream.IsPlaying)
            {
                _toneStream.SetVolume(volume, mute);
                _toneStream.SetPanning(panning);
            }

            // Register for export if playing
            if (_toneStream.IsPlaying && !_isRegistered)
            {
                AudioExportSourceRegistry.Register(this);
                _isRegistered = true;
            }
            else if (!_toneStream.IsPlaying && _isRegistered)
            {
                AudioExportSourceRegistry.Unregister(this);
                _isRegistered = false;
            }

            IsPlaying.Value = _toneStream.IsPlaying;
            GetLevel.Value = _toneStream.GetLevel();
            GetWaveform.Value = _toneStream.GetWaveform();
            GetSpectrum.Value = _toneStream.GetSpectrum();
        }

        private void EnsureToneStream()
        {
            if (_toneStream != null) return;

            if (AudioMixerManager.OperatorMixerHandle == 0)
            {
                AudioMixerManager.Initialize();
                if (AudioMixerManager.OperatorMixerHandle == 0)
                {
                    Log.Warning("[TestToneGenerator] Mixer not available");
                    return;
                }
            }

            _toneStream = ProceduralToneStream.Create(AudioMixerManager.OperatorMixerHandle);
            if (_toneStream != null)
                AudioConfig.LogAudioDebug($"[TestToneGenerator] Created procedural tone stream");
        }

        public int RenderAudio(double startTime, double duration, float[] buffer)
        {
            if (_toneStream == null || !_toneStream.IsPlaying)
            {
                Array.Clear(buffer, 0, buffer.Length);
                return buffer.Length;
            }

            return _toneStream.RenderAudio(startTime, duration, buffer);
        }

        ~TestToneGenerator()
        {
            if (_isRegistered)
                AudioExportSourceRegistry.Unregister(this);

            _toneStream?.Dispose();
        }

        private enum WaveformTypes
        {
            Sine = 0,
            Square = 1,
            Sawtooth = 2,
            Triangle = 3,
            WhiteNoise = 4,
            PinkNoise = 5
        }

        /// <summary>
        /// Procedural tone stream that generates waveforms in real-time via BASS callback stream.
        /// Uses a StreamProcedure callback to ensure continuous, accurate sample generation.
        /// Duration is tracked by counting generated samples for sample-accurate timing.
        /// </summary>
        private sealed class ProceduralToneStream
        {
            private const int SampleRate = 48000;
            private const int Channels = 2;

            // Thread-safe properties using volatile/interlocked
            private volatile float _frequency = 440f;
            private volatile int _waveformType;
            private volatile int _isPlaying;
            private volatile float _currentVolume = 1.0f;
            private volatile float _currentPanning;
            private volatile bool _isMuted;

            // Duration tracking (sample-accurate)
            private volatile float _durationSeconds = float.MaxValue;
            private long _samplesGenerated;
            private long _totalSamplesToGenerate;
            private volatile int _hasFinished;

            public float Frequency
            {
                get => _frequency;
                set => _frequency = Math.Max(20f, Math.Min(20000f, value));
            }

            public float DurationSeconds
            {
                get => _durationSeconds;
                set => _durationSeconds = value;
            }

            public WaveformTypes WaveformType
            {
                get => (WaveformTypes)_waveformType;
                set => _waveformType = (int)value;
            }

            public bool IsPlaying => _isPlaying == 1;
            public bool HasFinished => _hasFinished == 1;

            private int _streamHandle;
            private readonly int _mixerHandle;
            private double _phase;

            private readonly List<float> _waveformBuffer = new();
            private readonly List<float> _spectrumBuffer = new();
            private float _lastLevel;

            // Must keep a reference to prevent GC from collecting the delegate
            private readonly StreamProcedure _streamProc;
            private GCHandle _gcHandle;

            private ProceduralToneStream(int mixerHandle)
            {
                _mixerHandle = mixerHandle;
                _streamProc = StreamCallback;
            }

            public static ProceduralToneStream? Create(int mixerHandle)
            {
                var instance = new ProceduralToneStream(mixerHandle);

                // Pin the instance to prevent GC issues with the callback
                instance._gcHandle = GCHandle.Alloc(instance);

                // Create a callback stream for procedural audio
                var streamHandle = Bass.CreateStream(
                    SampleRate,
                    Channels,
                    BassFlags.Float | BassFlags.Decode,
                    instance._streamProc,
                    GCHandle.ToIntPtr(instance._gcHandle));

                if (streamHandle == 0)
                {
                    Log.Error($"[ProceduralToneStream] Failed to create stream: {Bass.LastError}");
                    instance._gcHandle.Free();
                    return null;
                }

                instance._streamHandle = streamHandle;

                // Add to mixer (paused initially)
                if (!BassMix.MixerAddChannel(mixerHandle, streamHandle, BassFlags.MixerChanBuffer | BassFlags.MixerChanPause))
                {
                    Log.Error($"[ProceduralToneStream] Failed to add to mixer: {Bass.LastError}");
                    Bass.StreamFree(streamHandle);
                    instance._gcHandle.Free();
                    return null;
                }

                Bass.ChannelSetAttribute(streamHandle, ChannelAttribute.Volume, 0f);
                return instance;
            }

            private int StreamCallback(int handle, IntPtr buffer, int length, IntPtr user)
            {
                int floatCount = length / sizeof(float);
                int sampleCount = floatCount / Channels;

                var floatBuffer = new float[floatCount];

                bool playing = _isPlaying == 1;
                float freq = _frequency;
                int waveType = _waveformType;
                float pan = _currentPanning;

                // Check duration limit
                long totalLimit = _totalSamplesToGenerate;
                bool hasLimit = totalLimit > 0 && totalLimit < long.MaxValue;
                long remaining = hasLimit ? Math.Max(0, totalLimit - _samplesGenerated) : long.MaxValue;

                // Determine effective volume (0 if not playing, finished, or muted)
                bool effectivelyPlaying = playing && (!hasLimit || remaining > 0);
                float vol = effectivelyPlaying && !_isMuted ? _currentVolume : 0f;

                double phaseIncrement = 2.0 * Math.PI * freq / SampleRate;

                int samplesToGenerate = hasLimit ? (int)Math.Min(sampleCount, remaining) : sampleCount;

                for (int i = 0; i < samplesToGenerate; i++)
                {
                    float sample = GenerateSampleStatic(_phase, waveType) * vol;
                    _phase += phaseIncrement;

                    // Keep phase in reasonable range to avoid precision loss
                    if (_phase >= 2.0 * Math.PI)
                        _phase -= 2.0 * Math.PI;

                    // Apply panning
                    float leftGain = pan <= 0 ? 1f : 1f - pan;
                    float rightGain = pan >= 0 ? 1f : 1f + pan;

                    floatBuffer[i * 2] = sample * leftGain;
                    floatBuffer[i * 2 + 1] = sample * rightGain;
                }

                // Fill remainder with silence if we hit the limit
                for (int i = samplesToGenerate; i < sampleCount; i++)
                {
                    floatBuffer[i * 2] = 0f;
                    floatBuffer[i * 2 + 1] = 0f;
                }

                // Track samples generated (only when playing)
                if (playing && hasLimit)
                {
                    _samplesGenerated += samplesToGenerate;

                    // Signal that we've finished
                    if (_samplesGenerated >= totalLimit)
                    {
                        Interlocked.Exchange(ref _hasFinished, 1);
                    }
                }

                // Copy to unmanaged buffer
                Marshal.Copy(floatBuffer, 0, buffer, floatCount);

                return length;
            }

            private static readonly Random _noiseRng = new();
            // Pink noise filter state
            private static double _pink_b0, _pink_b1, _pink_b2;

            private static float GenerateSampleStatic(double phase, int waveType)
            {
                // Normalize phase to [0, 2π)
                double normalizedPhase = phase - Math.Floor(phase / (2.0 * Math.PI)) * 2.0 * Math.PI;
                double t = normalizedPhase / (2.0 * Math.PI); // t in [0, 1)

                switch (waveType)
                {
                    case 0: // Sine
                        return (float)Math.Sin(normalizedPhase);
                    case 1: // Square
                        return t < 0.5 ? 0.8f : -0.8f;
                    case 2: // Sawtooth
                        return (float)(2.0 * t - 1.0) * 0.8f;
                    case 3: // Triangle
                        return (float)(4.0 * Math.Abs(t - 0.5) - 1.0) * 0.8f;
                    case 4: // WhiteNoise
                        return (float)(_noiseRng.NextDouble() * 2.0 - 1.0) * 0.5f;
                    case 5: // PinkNoise (simple filter)
                        {
                            // White noise input
                            double white = _noiseRng.NextDouble() * 2.0 - 1.0;
                            // Paul Kellet's simple pink filter
                            _pink_b0 = 0.99765 * _pink_b0 + white * 0.0990460;
                            _pink_b1 = 0.96300 * _pink_b1 + white * 0.2965164;
                            _pink_b2 = 0.57000 * _pink_b2 + white * 1.0526913;
                            double pink = _pink_b0 + _pink_b1 + _pink_b2 + white * 0.1848;
                            return (float)(pink * 0.15); // scale to avoid clipping
                        }
                    default:
                        return (float)Math.Sin(normalizedPhase);
                }
            }

            public void Play()
            {
                // Always restart on trigger (even if already playing)
                Interlocked.Exchange(ref _isPlaying, 1);

                _phase = 0;
                _samplesGenerated = 0;
                Interlocked.Exchange(ref _hasFinished, 0);

                // Calculate total samples based on duration
                float dur = _durationSeconds;
                _totalSamplesToGenerate = dur >= float.MaxValue / 2f
                    ? long.MaxValue
                    : (long)(dur * SampleRate);

                // Unpause the channel
                BassMix.ChannelFlags(_streamHandle, 0, BassFlags.MixerChanPause);
                Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, _isMuted ? 0f : _currentVolume);
            }

            public void Stop()
            {
                if (Interlocked.Exchange(ref _isPlaying, 0) == 0)
                    return; // Already stopped

                // Mute but keep channel running (don't pause - avoids buffer issues)
                Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, 0f);
                _phase = 0;
                _samplesGenerated = 0;
                Interlocked.Exchange(ref _hasFinished, 0);
            }

            public void SetVolume(float volume, bool mute)
            {
                _currentVolume = Math.Max(0f, Math.Min(1f, volume));
                _isMuted = mute;

                if (_isPlaying == 1)
                    Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Volume, mute ? 0f : _currentVolume);
            }

            public void SetPanning(float panning)
            {
                _currentPanning = Math.Clamp(panning, -1f, 1f);
                if (_isPlaying == 1)
                    Bass.ChannelSetAttribute(_streamHandle, ChannelAttribute.Pan, _currentPanning);
            }

            public float GetLevel()
            {
                if (_isPlaying != 1) return 0f;

                var level = BassMix.ChannelGetLevel(_streamHandle);
                if (level == -1) return _lastLevel;

                var left = (level & 0xFFFF) / 32768f;
                var right = ((level >> 16) & 0xFFFF) / 32768f;
                _lastLevel = Math.Min(Math.Max(left, right), 1f);
                return _lastLevel;
            }

            public List<float> GetWaveform()
            {
                if (_isPlaying != 1)
                {
                    EnsureBuffer(_waveformBuffer, 512);
                    return _waveformBuffer;
                }

                // Generate waveform preview based on current frequency
                _waveformBuffer.Clear();
                int waveType = _waveformType;
                for (int i = 0; i < 512; i++)
                {
                    double t = i / 512.0 * 4 * Math.PI; // Show ~2 cycles
                    float sample = GenerateSampleStatic(t, waveType);
                    _waveformBuffer.Add(Math.Abs(sample));
                }
                return _waveformBuffer;
            }

            public List<float> GetSpectrum()
            {
                if (_isPlaying != 1)
                {
                    EnsureBuffer(_spectrumBuffer, 512);
                    return _spectrumBuffer;
                }

                // Simple spectrum visualization for a pure tone
                _spectrumBuffer.Clear();
                float freq = _frequency;
                int freqBin = (int)(freq / (SampleRate / 2f) * 512);
                freqBin = Math.Clamp(freqBin, 0, 511);

                for (int i = 0; i < 512; i++)
                {
                    // Peak at the frequency bin, fall off around it
                    int dist = Math.Abs(i - freqBin);
                    float val = dist == 0 ? 1f : Math.Max(0f, 1f - dist * 0.1f);
                    _spectrumBuffer.Add(val * (_isMuted ? 0f : _currentVolume));
                }
                return _spectrumBuffer;
            }

            public int RenderAudio(double startTime, double duration, float[] buffer)
            {
                int sampleCount = buffer.Length / Channels;
                float freq = _frequency;
                int waveType = _waveformType;
                double phaseIncrement = 2.0 * Math.PI * freq / SampleRate;

                for (int i = 0; i < sampleCount; i++)
                {
                    float sample = GenerateSampleStatic(_phase, waveType) * _currentVolume;
                    _phase += phaseIncrement;

                    if (_phase >= 2.0 * Math.PI)
                        _phase -= 2.0 * Math.PI;

                    // Apply panning
                    float pan = _currentPanning;
                    float leftGain = pan <= 0 ? 1f : 1f - pan;
                    float rightGain = pan >= 0 ? 1f : 1f + pan;

                    buffer[i * 2] = sample * leftGain;
                    buffer[i * 2 + 1] = sample * rightGain;
                }

                return buffer.Length;
            }

            private static void EnsureBuffer(List<float> buffer, int size)
            {
                if (buffer.Count == 0)
                    for (int i = 0; i < size; i++) buffer.Add(0f);
            }

            public void Dispose()
            {
                Interlocked.Exchange(ref _isPlaying, 0);
                BassMix.MixerRemoveChannel(_streamHandle);
                Bass.StreamFree(_streamHandle);

                if (_gcHandle.IsAllocated)
                    _gcHandle.Free();
            }
        }
    }
}
