using T3.Core.Audio;

namespace Lib.io.audio
{
    [Guid("65e95f77-4743-437f-ab31-f34b831d28d7")]
    internal sealed class StereoAudioPlayer : Instance<StereoAudioPlayer>
    {
        [Input(Guid = "505139a0-71ce-4297-8440-5bf84488902e")]
        public readonly InputSlot<string> AudioFile = new();

        [Input(Guid = "726bc4d3-df8b-4abe-a38e-2e09cf44ca10")]
        public readonly InputSlot<bool> PlayAudio = new();

        [Input(Guid = "59b659c6-ca1f-4c2b-8dff-3a1da9abd352")]
        public readonly InputSlot<bool> StopAudio = new();

        [Input(Guid = "7e42f2a8-3c5d-4f6e-9b8a-1d2e3f4a5b6c")]
        public readonly InputSlot<bool> PauseAudio = new();

        [Input(Guid = "c0645e37-db4e-4658-9d65-96478851f6f6")]
        public readonly InputSlot<float> Volume = new();
 
        [Input(Guid = "1a3f4b7c-12d3-4a5b-9c7d-8e1f2a3b4c5d")]
        public readonly InputSlot<bool> Mute = new();

        [Input(Guid = "53d1622e-b1d5-4b1c-acd0-ebceb7064043")]
        public readonly InputSlot<float> Panning = new();

        [Input(Guid = "a5de0d72-5924-4f3a-a02f-d5de7c03f07f")]
        public readonly InputSlot<float> Seek = new();


        [Output(Guid = "2433f838-a8ba-4f3a-809e-2d41c404bb84")]
        public readonly Slot<Command> Result = new();

        [Output(Guid = "960aa0a3-89b4-4eff-8b52-36ff6965cf8f")]
        public readonly Slot<bool> IsPlaying = new();

        [Output(Guid = "3f8a9c2e-5d7b-4e1f-a6c8-9d2e1f3b5a7c")]
        public readonly Slot<bool> IsPaused = new();

        [Output(Guid = "b09d215a-bcf0-479a-a649-56f9c698ecb1")]
        public readonly Slot<float> GetLevel = new();

        private Guid _operatorId;
        private bool _wasPausedLastFrame;

        public StereoAudioPlayer()
        {
            Result.UpdateAction += Update;
        }

        private void Update(EvaluationContext context)
        {
            if (_operatorId == Guid.Empty)
            {
                _operatorId = ComputeInstanceGuid();
            }

            var filePath = AudioFile.GetValue(context);
            var shouldPlay = PlayAudio.GetValue(context);
            var shouldStop = StopAudio.GetValue(context);
            var shouldPause = PauseAudio.GetValue(context);
            var volume = Volume.GetValue(context);
            var mute = Mute.GetValue(context);
            var panning = Panning.GetValue(context);

            // Handle pause/resume transitions
            var pauseStateChanged = shouldPause != _wasPausedLastFrame;
            if (pauseStateChanged)
            {
                if (shouldPause)
                {
                    AudioEngine.PauseOperator(_operatorId);
                }
                else
                {
                    AudioEngine.ResumeOperator(_operatorId);
                }
            }
            _wasPausedLastFrame = shouldPause;

            // Send all state to AudioEngine - let it handle the logic
            AudioEngine.UpdateOperatorPlayback(
                operatorId: _operatorId,
                localFxTime: context.LocalFxTime,
                filePath: filePath,
                shouldPlay: shouldPlay,
                shouldStop: shouldStop,
                volume: volume,
                mute: mute,
                panning: panning
            );

            // Get outputs from engine
            IsPlaying.Value = AudioEngine.IsOperatorStreamPlaying(_operatorId);
            IsPaused.Value = AudioEngine.IsOperatorPaused(_operatorId);
            GetLevel.Value = AudioEngine.GetOperatorLevel(_operatorId);
        }

        private Guid ComputeInstanceGuid()
        {
            unchecked
            {
                ulong hash = 0xCBF29CE484222325;
                const ulong prime = 0x100000001B3;

                foreach (var id in InstancePath)
                {
                    var bytes = id.ToByteArray();
                    foreach (var b in bytes)
                    {
                        hash ^= b;
                        hash *= prime;
                    }
                }

                var guidBytes = new byte[16];
                var hashBytes = BitConverter.GetBytes(hash);
                Array.Copy(hashBytes, 0, guidBytes, 0, 8);
                Array.Copy(hashBytes, 0, guidBytes, 8, 8);
                return new Guid(guidBytes);
            }
        }

        ~StereoAudioPlayer()
        {
            if (_operatorId != Guid.Empty)
            {
                AudioEngine.UnregisterOperator(_operatorId);
            }
        }
    }
}