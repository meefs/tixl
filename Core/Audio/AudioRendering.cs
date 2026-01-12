using System;
using System.Collections.Generic;
using System.Linq;
using ManagedBass;
using T3.Core.Animation;
using System.Runtime.InteropServices;

namespace T3.Core.Audio;

public static class AudioRendering
{
    public static void PrepareRecording(Playback playback, double fps)
    {
        if (AudioConfig.ShowRenderLogs)
            Logging.Log.Debug($"[AudioRendering] PrepareRecording called with fps={fps}", typeof(AudioRendering));

        _settingsBeforeExport.BassUpdateThreads = Bass.GetConfig(Configuration.UpdateThreads);
        _settingsBeforeExport.BassUpdatePeriodInMs = Bass.GetConfig(Configuration.UpdatePeriod);
        _settingsBeforeExport.BassGlobalStreamVolume = Bass.GetConfig(Configuration.GlobalStreamVolume);

        // Turn off automatic sound generation
        Bass.Pause();
        Bass.Configure(Configuration.UpdateThreads, false);
        Bass.Configure(Configuration.UpdatePeriod, 0);
        Bass.Configure(Configuration.GlobalStreamVolume, 0);

        foreach (var (_, clipStream) in AudioEngine.SoundtrackClipStreams)
        {
            _settingsBeforeExport.BufferLengthInSeconds = Bass.ChannelGetAttribute(clipStream.StreamHandle, ChannelAttribute.Buffer);

            Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.Volume, 1.0);
            Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.Buffer, 1.0 / fps);

            // TODO: Find this in Managed Bass library. It doesn't seem to be present.
            const int tailAttribute = 16;
            Bass.ChannelSetAttribute(clipStream.StreamHandle, (ChannelAttribute)tailAttribute, 2.0 / fps);
            Bass.ChannelStop(clipStream.StreamHandle);
            clipStream.UpdateTimeWhileRecording(playback, fps, true);
            Bass.ChannelPlay(clipStream.StreamHandle);
            Bass.ChannelPause(clipStream.StreamHandle);
        }

        _fifoBuffersForClips.Clear();
    }

    internal static void ExportAudioFrame(Playback playback, double frameDurationInSeconds, SoundtrackClipStream clipStream)
    {
        if (AudioConfig.ShowRenderLogs)
            Logging.Log.Debug($"[AudioRendering] ExportAudioFrame called for clip {clipStream.ResourceHandle} with frameDuration={frameDurationInSeconds}", typeof(AudioRendering));
        try
        {
            if (!_fifoBuffersForClips.TryGetValue(clipStream.ResourceHandle, out var bufferQueue))
            {
                bufferQueue = new Queue<byte>();
                _fifoBuffersForClips[clipStream.ResourceHandle] = bufferQueue;
            }

            // Update time position in clip
            var streamPositionInBytes = clipStream.UpdateTimeWhileRecording(playback, 1.0 / frameDurationInSeconds, true);
            var bytes = (int)Math.Max(Bass.ChannelSeconds2Bytes(clipStream.StreamHandle, frameDurationInSeconds), 0);
            if (bytes > 0)
            {
                // If stream position is negative, add silence at the beginning
                if (streamPositionInBytes < 0)
                {
                    var silenceBytesToAdd = Math.Min(-streamPositionInBytes, bytes);
                    for (int i = 0; i < silenceBytesToAdd; i++)
                        bufferQueue.Enqueue(0);
                }

                // Set the channel buffer size and update
                Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.Buffer,
                                         (int)Math.Round(frameDurationInSeconds * 1000.0));
                Bass.ChannelUpdate(clipStream.StreamHandle, (int)Math.Round(frameDurationInSeconds * 1000.0));

                // Read audio data in correct format
                var info = Bass.ChannelGetInfo(clipStream.StreamHandle);
                byte[] validData = null;
                if ((info.Flags & BassFlags.Float) != 0)
                {
                    int floatCount = bytes / sizeof(float);
                    var floatBuffer = new float[floatCount];
                    int floatBytesRead = Bass.ChannelGetData(clipStream.StreamHandle, floatBuffer, bytes);
                    if (floatBytesRead > 0 && floatBytesRead <= bytes)
                    {
                        validData = new byte[floatBytesRead];
                        Buffer.BlockCopy(floatBuffer, 0, validData, 0, floatBytesRead);
                    }
                }
                else
                {
                    var newBuffer = new byte[bytes];
                    var newBytes = Bass.ChannelGetData(clipStream.StreamHandle, newBuffer, bytes);
                    if (newBytes > 0 && newBytes <= bytes)
                    {
                        validData = new byte[newBytes];
                        Array.Copy(newBuffer, validData, newBytes);
                    }
                }
                if (validData != null)
                {
                    foreach (var b in validData)
                        bufferQueue.Enqueue(b);
                    AudioEngine.UpdateFftBufferFromSoundtrack(clipStream.StreamHandle, playback);
                }

                // If buffer is too short, add silence
                while (bufferQueue.Count < bytes)
                    bufferQueue.Enqueue(0);
                // If buffer is too long, remove extra
                while (bufferQueue.Count > bytes)
                    bufferQueue.Dequeue();
            }

            if (AudioConfig.ShowRenderLogs)
                Logging.Log.Debug($"[AudioRendering] Exported {bytes} bytes for clip {clipStream.ResourceHandle}", typeof(AudioRendering));
        }
        catch (Exception ex)
        {
            Logging.Log.Error($"ExportAudioFrame error: {ex.Message}", typeof(AudioRendering));
        }
    }

    public static void EndRecording(Playback playback, double fps)
    {
        if (AudioConfig.ShowRenderLogs)
            Logging.Log.Debug($"[AudioRendering] EndRecording called with fps={fps}", typeof(AudioRendering));
        // TODO: Find this in Managed Bass library. It doesn't seem to be present.
        const int tailAttribute = 16;

        foreach (var (_, clipStream) in AudioEngine.SoundtrackClipStreams)
        {
            // Bass.ChannelPause(clipStream.StreamHandle);
            clipStream.UpdateTimeWhileRecording(playback, fps, false);
            Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.NoRamp, 0);
            Bass.ChannelSetAttribute(clipStream.StreamHandle, (ChannelAttribute)tailAttribute, 0.0);
            Bass.ChannelSetAttribute(clipStream.StreamHandle, ChannelAttribute.Buffer, _settingsBeforeExport.BufferLengthInSeconds);
        }

        // restore live playback values
        Bass.Configure(Configuration.UpdatePeriod, _settingsBeforeExport.BassUpdatePeriodInMs);
        Bass.Configure(Configuration.GlobalStreamVolume, _settingsBeforeExport.BassGlobalStreamVolume);
        Bass.Configure(Configuration.UpdateThreads, _settingsBeforeExport.BassUpdateThreads);
        Bass.Start();
    }

    public static byte[] GetLastMixDownBuffer(double frameDurationInSeconds)
    {
        if (AudioConfig.ShowRenderLogs)
            Logging.Log.Debug($"[AudioRendering] GetLastMixDownBuffer called with frameDuration={frameDurationInSeconds}", typeof(AudioRendering));
        try
        {
            if (AudioEngine.SoundtrackClipStreams.Count == 0)
            {
                // Get default sample rate
                var channels = AudioEngine.GetClipChannelCount(null);
                var sampleRate = AudioEngine.GetClipSampleRate(null);
                var samples = (int)Math.Max(Math.Round(frameDurationInSeconds * sampleRate), 0.0);
                var bytes = samples * channels * sizeof(float);
                return new byte[bytes];
            }

            foreach (var (_, clipStream) in AudioEngine.SoundtrackClipStreams)
            {
                if (!_fifoBuffersForClips.TryGetValue(clipStream.ResourceHandle, out var bufferQueue))
                    continue;

                var bytes = (int)Bass.ChannelSeconds2Bytes(clipStream.StreamHandle, frameDurationInSeconds);
                var result = new byte[bytes];
                for (int i = 0; i < bytes; i++)
                {
                    result[i] = bufferQueue.Count > 0 ? bufferQueue.Dequeue() : (byte)0;
                }
                return result;
            }
            Logging.Log.Error("GetLastMixDownBuffer: No valid audio buffer found.", typeof(AudioRendering));
            return null;
        }
        catch (Exception ex)
        {
            Logging.Log.Error($"GetLastMixDownBuffer error: {ex.Message}", typeof(AudioRendering));
            return null;
        }
    }

    /// <summary>
    /// Mixes all registered IAudioExportSource audio into the given float buffer for export.
    /// </summary>
    /// <param name="startTime">Start time in seconds (localFxTime).</param>
    /// <param name="duration">Duration in seconds.</param>
    /// <param name="buffer">Buffer to fill (interleaved float samples, stereo).</param>
    public static void MixExportSourcesIntoBuffer(double startTime, double duration, float[] buffer)
    {
        Array.Clear(buffer, 0, buffer.Length);
        foreach (var source in AudioExportSourceRegistry.Sources)
        {
            // Temporary buffer for each source
            float[] temp = new float[buffer.Length];
            int written = source.RenderAudio(startTime, duration, temp);
            // Mix (sum) into output buffer
            for (int i = 0; i < buffer.Length; i++)
                buffer[i] += temp[i];
        }
    }

    /// <summary>
    /// Get the final audio buffer for the current frame, including soundtrack and all registered export sources.
    /// This version decodes soundtrack clips directly for export, not relying on live BASS playback.
    /// </summary>
    /// <param name="frameDurationInSeconds">Duration of the frame in seconds.</param>
    /// <param name="localFxTime">The localFxTime for this frame (for operator sync).</param>
    /// <returns>Interleaved float buffer (stereo) for the frame.</returns>
    public static float[] GetFullMixDownBuffer(double frameDurationInSeconds, double localFxTime)
    {
        if (AudioConfig.ShowRenderLogs)
            Logging.Log.Debug($"[AudioRendering] GetFullMixDownBuffer called with frameDuration={frameDurationInSeconds}, localFxTime={localFxTime}", typeof(AudioRendering));
        int mixerSampleRate = AudioConfig.MixerFrequency;
        int channels = AudioEngine.GetClipChannelCount(null);
        int sampleCount = (int)Math.Max(Math.Round(frameDurationInSeconds * mixerSampleRate), 0.0);
        int floatCount = sampleCount * channels;
        float[] mixBuffer = new float[floatCount];
        if (AudioConfig.ShowRenderLogs)
            Logging.Log.Debug($"[AudioRendering] Mixer sample rate: {mixerSampleRate}, channels: {channels}, frameDuration: {frameDurationInSeconds}, floatCount: {floatCount}", typeof(AudioRendering));

        // 1. Mix all soundtrack clips (decode directly)
        foreach (var (_, clipStream) in AudioEngine.SoundtrackClipStreams)
        {
            var handle = clipStream.ResourceHandle;
            if (!handle.TryGetFileResource(out var file) || file.FileInfo == null)
                continue;
            string filePath = file.FileInfo.FullName;
            if (!System.IO.File.Exists(filePath))
                continue;
            int decodeStream = Bass.CreateStream(filePath, Flags: BassFlags.Decode | BassFlags.Float);
            if (decodeStream == 0)
                continue;
            // Compute start time for this clip
            double clipStart = handle.Clip.StartTime;
            double timeInClip = localFxTime - clipStart;
            if (timeInClip < 0 || timeInClip > handle.Clip.LengthInSeconds)
            {
                Bass.StreamFree(decodeStream);
                continue;
            }
            // Get the sample rate of the clip
            Bass.ChannelGetInfo(decodeStream, out var info);
            int clipSampleRate = info.Frequency;
            int clipChannels = info.Channels;
            if (AudioConfig.ShowRenderLogs)
                Logging.Log.Debug($"[AudioRendering] Decoding clip: {filePath}, clipSampleRate: {clipSampleRate}, clipChannels: {clipChannels}, timeInClip: {timeInClip}", typeof(AudioRendering));
            Bass.ChannelSetPosition(decodeStream, Bass.ChannelSeconds2Bytes(decodeStream, timeInClip));
            int clipSampleCount = (int)Math.Max(Math.Round(frameDurationInSeconds * clipSampleRate), 0.0);
            int clipFloatCount = clipSampleCount * clipChannels;
            float[] temp = new float[clipFloatCount];
            int bytesToRead = clipFloatCount * sizeof(float);
            int bytesRead = Bass.ChannelGetData(decodeStream, temp, bytesToRead);
            Bass.StreamFree(decodeStream);
            float volume = handle.Clip.Volume;
            int samplesRead = bytesRead / sizeof(float);
            // Resample if needed
            float[] resampled = temp;
            if (clipSampleRate != mixerSampleRate && samplesRead > 0)
            {
                int resampleSamples = (int)Math.Max(Math.Round(frameDurationInSeconds * mixerSampleRate), 0.0);
                if (AudioConfig.ShowRenderLogs)
                    Logging.Log.Debug($"[AudioRendering] Resampling from {clipSampleRate} to {mixerSampleRate}, inputSamples: {samplesRead / clipChannels}, outputSamples: {resampleSamples}", typeof(AudioRendering));
                resampled = LinearResample(temp, samplesRead / clipChannels, clipChannels, resampleSamples, channels);
                samplesRead = resampleSamples * channels;
            }
            // Mix into output buffer
            for (int i = 0; i < Math.Min(samplesRead, floatCount); i++)
                mixBuffer[i] += resampled[i] * volume;
            // Zero out the rest if not enough samples were read
            for (int i = samplesRead; i < floatCount; i++)
                mixBuffer[i] += 0f;
        }

        // 2. Mix in all registered export sources (do not clear mixBuffer!)
        float[] opTemp = new float[floatCount];
        foreach (var source in AudioExportSourceRegistry.Sources)
        {
            Array.Clear(opTemp, 0, opTemp.Length);
            int written = source.RenderAudio(localFxTime, frameDurationInSeconds, opTemp);
            string opType = source.GetType().Name;
            string filePath = null;
            if (opType == "StereoAudioPlayer" || opType == "SpatialAudioPlayer")
            {
                var filePathProp = source.GetType().GetProperty("CurrentFilePath");
                if (filePathProp != null)
                {
                    filePath = filePathProp.GetValue(source) as string;
                }
            }
            // --- Metering: update from buffer if possible ---
            var updateFromBufferMethod = source.GetType().GetMethod("UpdateFromBuffer");
            if (updateFromBufferMethod != null)
            {
                updateFromBufferMethod.Invoke(source, new object[] { opTemp });
            }
            if (AudioConfig.ShowRenderLogs)
            {
                if (!string.IsNullOrEmpty(filePath))
                {
                    int sampleRate = mixerSampleRate;
                    int ch = channels;
                    Logging.Log.Debug($"[AudioRendering] Decoding operator: {opType}, file: {filePath}, sampleRate: {sampleRate}, channels: {ch}, time: {localFxTime}", typeof(AudioRendering));
                }
                else
                {
                    Logging.Log.Debug($"[AudioRendering] Decoding operator: {opType}, file: -, sampleRate: {mixerSampleRate}, channels: {channels}, time: {localFxTime}", typeof(AudioRendering));
                }
            }
            for (int i = 0; i < written && i < floatCount; i++)
                mixBuffer[i] += opTemp[i];
        }
        return mixBuffer;
    }

    // Simple linear resampler for float[] audio (interleaved)
    private static float[] LinearResample(float[] input, int inputSamples, int inputChannels, int outputSamples, int outputChannels)
    {
        float[] output = new float[outputSamples * outputChannels];
        for (int ch = 0; ch < Math.Min(inputChannels, outputChannels); ch++)
        {
            for (int i = 0; i < outputSamples; i++)
            {
                float t = (float)i / (outputSamples - 1);
                float srcPos = t * (inputSamples - 1);
                int srcIndex = (int)srcPos;
                float frac = srcPos - srcIndex;
                int srcBase = srcIndex * inputChannels + ch;
                int srcNext = Math.Min(srcIndex + 1, inputSamples - 1) * inputChannels + ch;
                float sampleA = input[srcBase];
                float sampleB = input[srcNext];
                output[i * outputChannels + ch] = sampleA + (sampleB - sampleA) * frac;
            }
        }
        // If outputChannels > inputChannels, fill extra channels with 0
        if (outputChannels > inputChannels)
        {
            for (int ch = inputChannels; ch < outputChannels; ch++)
                for (int i = 0; i < outputSamples; i++)
                    output[i * outputChannels + ch] = 0f;
        }
        return output;
    }

    /// <summary>
    /// Call this after export to clean up all operator decode streams.
    /// </summary>
    public static void CleanupExportOperatorStreams()
    {
        foreach (var source in AudioExportSourceRegistry.Sources)
        {
            var cleanupMethod = source.GetType().GetMethod("CleanupExportDecodeStream");
            cleanupMethod?.Invoke(source, null);
            // Clear export metering if available
            var clearExportMetering = source.GetType().GetMethod("ClearExportMetering");
            clearExportMetering?.Invoke(source, null);
        }
        T3.Core.Audio.AudioEngine.ClearAllExportMetering();
        T3.Core.Audio.AudioEngine.ResumeAllOperators();
        T3.Core.Audio.AudioEngine.ClearStaleMutedForAllOperators();
        T3.Core.Audio.AudioEngine.ForcePlayAllOperators();
        T3.Core.Audio.AudioEngine.RestoreMixerRoutingAndPlaybackForAllOperators();
        T3.Core.Audio.AudioRendering.RestoreOperatorVolumesAfterExport();
        T3.Core.Audio.AudioEngine.ForceMeteringUpdateForAllOperators();
        T3.Core.Audio.AudioEngine.ForceOperatorOutputUpdate();
    }

    /// <summary>
    /// Force evaluation of metering outputs for all registered audio export sources during export.
    /// Call this after each audio frame is rendered.
    /// </summary>
    public static void EvaluateAllAudioMeteringOutputs(double localFxTime, float[]? audioBuffer = null)
    {
        // Use the constructor and Reset() to set Playback, since Playback has a private setter
        var context = new T3.Core.Operator.EvaluationContext();
        context.LocalFxTime = localFxTime;
        // context.Playback is set to Playback.Current by default in Reset()
        foreach (var source in AudioExportSourceRegistry.Sources)
        {
            var type = source.GetType();
            var getLevelProp = type.GetProperty("GetLevel");
            var getWaveformProp = type.GetProperty("GetWaveform");
            var getSpectrumProp = type.GetProperty("GetSpectrum");
            var getLevel = getLevelProp?.GetValue(source);
            var getWaveform = getWaveformProp?.GetValue(source);
            var getSpectrum = getSpectrumProp?.GetValue(source);

            // Try to update from buffer if possible
            if (audioBuffer != null)
            {
                var updateFromBuffer = getLevel?.GetType().GetMethod("UpdateFromBuffer");
                updateFromBuffer?.Invoke(getLevel, new object[] { audioBuffer });
                updateFromBuffer = getWaveform?.GetType().GetMethod("UpdateFromBuffer");
                updateFromBuffer?.Invoke(getWaveform, new object[] { audioBuffer });
                updateFromBuffer = getSpectrum?.GetType().GetMethod("UpdateFromBuffer");
                updateFromBuffer?.Invoke(getSpectrum, new object[] { audioBuffer });
            }

            // Call GetValue(context) to update output slots as fallback
            getLevel?.GetType().GetMethod("GetValue")?.Invoke(getLevel, new object[] { context });
            getWaveform?.GetType().GetMethod("GetValue")?.Invoke(getWaveform, new object[] { context });
            getSpectrum?.GetType().GetMethod("GetValue")?.Invoke(getSpectrum, new object[] { context });
        }
    }

    /// <summary>
    /// Restores the stream volume for all registered audio export sources to the value set in the operator's Volume input slot after export.
    /// </summary>
    public static void RestoreOperatorVolumesAfterExport()
    {
        foreach (var source in AudioExportSourceRegistry.Sources)
        {
            var typeName = source.GetType().Name;
            if (typeName == "StereoAudioPlayer" || typeName == "SpatialAudioPlayer")
            {
                var restoreMethod = source.GetType().GetMethod("RestoreVolumeAfterExport");
                restoreMethod?.Invoke(source, null);
            }
        }
    }

    private static readonly Dictionary<AudioClipResourceHandle, Queue<byte>> _fifoBuffersForClips = new();

    private static BassSettingsBeforeExport _settingsBeforeExport;

    private struct BassSettingsBeforeExport
    {
        public int BassUpdatePeriodInMs; // initial Bass library update period in MS
        public int BassGlobalStreamVolume; // initial Bass library sample volume (range 0 to 10000)
        public int BassUpdateThreads; // initial Bass library update threads
        public double BufferLengthInSeconds; // FIXME: Why is that a single attribute for all clip streams?
    }
}