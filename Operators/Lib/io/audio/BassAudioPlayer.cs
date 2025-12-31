
using ManagedBass;


namespace Lib.io.audio{
    [Guid("65e95f77-4743-437f-ab31-f34b831d28d7")]
    internal sealed class BassAudioPlayer : Instance<BassAudioPlayer>
    {
        [Input(Guid = "505139a0-71ce-4297-8440-5bf84488902e")]
        private readonly InputSlot<string> AudioFile = new();

        [Input(Guid = "726bc4d3-df8b-4abe-a38e-2e09cf44ca10")]
        private readonly InputSlot<bool> PlayAudio = new();
        
        [Input(Guid = "59b659c6-ca1f-4c2b-8dff-3a1da9abd352")]
        private readonly InputSlot<bool> StopAudio = new();
        
        [Input(Guid = "c0645e37-db4e-4658-9d65-96478851f6f6")]
        private readonly InputSlot<float> Volume = new();
        
        [Input(Guid = "53d1622e-b1d5-4b1c-acd0-ebceb7064043")]
        private readonly InputSlot<float> Panning = new();
        
        [Input(Guid = "d1a11c4c-9526-4f6b-873e-1798b9dd2b48")]
        private readonly InputSlot<float> Speed = new();
        
        [Input(Guid = "a5de0d72-5924-4f3a-a02f-d5de7c03f07f")]
        private readonly InputSlot<float> Seek = new();

        [Output(Guid = "2433f838-a8ba-4f3a-809e-2d41c404bb84")]
        private readonly Slot<Command> Result = new();

        private int _stream;
        private static bool _bassInitialized;
        private bool _prevPlay;
        private bool _prevStop;
        private float _prevVolume = 1f;
        private float _prevPanning;
        private float _prevSpeed = 1f;
        private float _prevSeek;

        public BassAudioPlayer()
        {
            Result.UpdateAction += Update; 
        }

        private void Update(EvaluationContext context)
        {
            var filePath = AudioFile.GetValue(context);
            var shouldPlay = PlayAudio.GetValue(context);
            var shouldStop = StopAudio.GetValue(context);
            var volume = Volume.GetValue(context);
            var panning = Panning.GetValue(context);
            var speed = Speed.GetValue(context);
            var seek = Seek.GetValue(context);

            var playTrigger = shouldPlay && !_prevPlay;
            _prevPlay = shouldPlay;

            var stopTrigger = shouldStop && !_prevStop;
            _prevStop = shouldStop;

            if (!_bassInitialized)
            {
                Log.Debug("Initializing BASS...");
                if (!Bass.Init())
                {
                    if (Bass.LastError == Errors.Already)
                    {
                        Log.Debug($"BASS Already Initialized");
                        _bassInitialized = true;
                    }
                    else 
                    {
                        Log.Debug($"BASS.Init failed: {Bass.LastError}");
                        return;
                    }
                }
                else
                {
                    Log.Debug("BASS initialized successfully");
                    _bassInitialized = true;
                }
            }

            // Auto-free if stream completed playback
            if (_stream != 0 && Bass.ChannelIsActive(_stream) == PlaybackState.Stopped)
            {
                Log.Debug($"Stream {_stream} ended, freeing");
                Bass.StreamFree(_stream);
                _stream = 0;
            }

            // Stop trigger: stop and dispose the stream
            if (stopTrigger && _stream != 0)
            {
                Log.Debug($"Stop trigger: Stopping and freeing stream {_stream}");
                Bass.ChannelStop(_stream);
                Bass.StreamFree(_stream);
                _stream = 0;
            }

            // Apply controls only if stream exists and parameters changed
            if (_stream != 0)
            {
                // Volume (0-1+ range, only if changed)
                if (Math.Abs(volume - _prevVolume) > 0.001f)
                {
                    var clampedVolume = Math.Max(0f, volume);
                    Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, clampedVolume);
                    _prevVolume = volume;
                    Log.Debug($"Volume: {clampedVolume}");
                }
                
                // Panning (-1 to +1 range, only if changed)
                if (Math.Abs(panning - _prevPanning) > 0.001f)
                {
                    var clampedPanning = Math.Max(-1f, Math.Min(1f, panning));
                    Bass.ChannelSetAttribute(_stream, ChannelAttribute.Pan, clampedPanning);
                    _prevPanning = panning;
                    Log.Debug($"Panning: {clampedPanning}");
                }
                
                // Speed via frequency adjustment (simple pitch-preserving alternative)
                if (Math.Abs(speed - _prevSpeed) > 0.001f)
                {
                    var clampedSpeed = Math.Max(0.1f, Math.Min(4f, speed));
                    var freq = Bass.ChannelGetAttribute(_stream, ChannelAttribute.Frequency);
                    var newFreq = freq * clampedSpeed;
                    Bass.ChannelSetAttribute(_stream, ChannelAttribute.Frequency, newFreq);
                    _prevSpeed = speed;
                    Log.Debug($"Speed: {clampedSpeed} (freq: {newFreq})");
                }
                
                // Seek (0-1 normalized position, only if changed significantly)
                if (Math.Abs(seek - _prevSeek) > 0.001f && seek is >= 0f and <= 1f)
                {
                    var length = Bass.ChannelGetLength(_stream);
                    var seekPos = (long)(seek * length);
                    Bass.ChannelSetPosition(_stream, seekPos);
                    _prevSeek = seek;
                    Log.Debug($"Seek to {seek} ({seekPos}/{length})");
                }
            }

            // Play trigger: load/create stream
            if (!playTrigger || string.IsNullOrEmpty(filePath)) return;
            {
                if (_stream != 0)
                {
                    Log.Debug($"Play trigger while playing: restarting stream {_stream}");
                    Bass.ChannelSetPosition(_stream, 0);
                    Bass.ChannelPlay(_stream, true);
                }
                else
                {
                    Log.Debug($"Play trigger: Loading and playing {filePath}");
                    _stream = Bass.CreateStream(filePath);
                    
                    if (_stream != 0)
                    {
                        // Initialize with current parameters
                        Bass.ChannelSetAttribute(_stream, ChannelAttribute.Volume, Math.Max(0f, volume));
                        Bass.ChannelSetAttribute(_stream, ChannelAttribute.Pan, Math.Max(-1f, Math.Min(1f, panning)));
                        var freq = Bass.ChannelGetAttribute(_stream, ChannelAttribute.Frequency);
                        Bass.ChannelSetAttribute(_stream, ChannelAttribute.Frequency, freq * speed);
                        
                        Bass.ChannelPlay(_stream);
                        _prevVolume = volume;
                        _prevPanning = panning;
                        _prevSpeed = speed;
                        _prevSeek = 0f;
                        Log.Debug($"Started stream {_stream}");
                    }
                    else 
                    {
                        Log.Debug($"Failed to create stream: {Bass.LastError}");
                    }
                }
            }
        }

        ~BassAudioPlayer()
        {
            if (_stream != 0)
            {
                Bass.StreamFree(_stream);
                _stream = 0;
            }

            if (!_bassInitialized) return;
            Log.Debug("Freeing BASS");
            Bass.Free();
            _bassInitialized = false;
        }
    }
}
