#nullable enable
using System;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using ManagedBass;

namespace T3.Core.Audio;

/// <summary>
/// Represents a 3D spatial audio stream for operator-based playback with 3D positioning.
/// </summary>
public sealed class SpatialOperatorAudioStream : OperatorAudioStreamBase
{
    /// <summary>
    /// The 3D position of the audio source.
    /// </summary>
    private Vector3 _position = Vector3.Zero;
    
    /// <summary>
    /// The velocity of the audio source for Doppler effect calculations.
    /// </summary>
    private Vector3 _velocity = Vector3.Zero;
    
    /// <summary>
    /// The orientation direction of the audio source.
    /// </summary>
    private Vector3 _orientation = new(0, 0, -1);
    
    /// <summary>
    /// The minimum distance at which the audio starts to attenuate.
    /// </summary>
    private float _minDistance = 1.0f;
    
    /// <summary>
    /// The maximum distance at which the audio is no longer audible.
    /// </summary>
    private float _maxDistance = 100.0f;
    
    /// <summary>
    /// The 3D processing mode for the audio source.
    /// </summary>
    private Mode3D _3dMode = Mode3D.Normal;
    
    /// <summary>
    /// The inner cone angle in degrees within which audio is at full volume.
    /// </summary>
    private float _innerAngleDegrees = 360.0f;
    
    /// <summary>
    /// The outer cone angle in degrees beyond which audio is at the outer volume.
    /// </summary>
    private float _outerAngleDegrees = 360.0f;
    
    /// <summary>
    /// The volume level outside the outer cone (0.0 to 1.0).
    /// </summary>
    private float _outerVolume = 1.0f;

    /// <summary>
    /// Private constructor to enforce factory method usage.
    /// </summary>
    private SpatialOperatorAudioStream() { }

    /// <summary>
    /// Attempts to load a spatial audio stream from a file.
    /// </summary>
    /// <param name="filePath">The path to the audio file to load.</param>
    /// <param name="mixerHandle">The BASS mixer handle to add the stream to.</param>
    /// <param name="stream">When successful, contains the created spatial audio stream.</param>
    /// <returns><c>true</c> if the stream was successfully loaded; otherwise, <c>false</c>.</returns>
    internal static bool TryLoadStream(string filePath, int mixerHandle, [NotNullWhen(true)] out SpatialOperatorAudioStream? stream)
    {
        stream = null;

        // Load as mono for 3D audio
        if (!TryLoadStreamCore(filePath, mixerHandle, BassFlags.Mono,
            out var streamHandle, out var defaultFreq, out var info, out var duration))
        {
            return false;
        }

        stream = new SpatialOperatorAudioStream
        {
            StreamHandle = streamHandle,
            MixerStreamHandle = mixerHandle,
            DefaultPlaybackFrequency = defaultFreq,
            Duration = duration,
            FilePath = filePath,
            IsPlaying = false,
            IsPaused = false,
            CachedChannels = info.Channels,
            CachedFrequency = info.Frequency,
            IsStaleMuted = true
        };

        stream.Initialize3DAudio();
        AudioConfig.LogAudioDebug($"[SpatialAudioPlayer] Loaded: '{Path.GetFileName(filePath)}' ({info.Channels}ch, {info.Frequency}Hz, {duration:F2}s)");
        return true;
    }

    /// <summary>
    /// Initializes the 3D audio attributes and position for this stream.
    /// </summary>
    private void Initialize3DAudio()
    {
        Bass.ChannelSet3DAttributes(StreamHandle, _3dMode, _minDistance, _maxDistance,
            (int)_innerAngleDegrees, (int)_outerAngleDegrees, _outerVolume);
        Bass.ChannelSet3DPosition(StreamHandle, To3DVector(_position), To3DVector(_orientation), To3DVector(_velocity));
    }

    /// <summary>
    /// Converts a <see cref="Vector3"/> to a BASS <see cref="Vector3D"/>.
    /// </summary>
    /// <param name="v">The vector to convert.</param>
    /// <returns>The converted BASS 3D vector.</returns>
    private static Vector3D To3DVector(Vector3 v) => new(v.X, v.Y, v.Z);

    /// <summary>
    /// Updates the 3D position of the audio source and recalculates velocity.
    /// </summary>
    /// <param name="position">The new position of the audio source.</param>
    /// <param name="minDistance">The minimum distance for audio attenuation.</param>
    /// <param name="maxDistance">The maximum distance for audio attenuation.</param>
    internal void Update3DPosition(Vector3 position, float minDistance, float maxDistance)
    {
        var deltaPos = position - _position;
        _velocity = deltaPos * 60.0f; // Assume ~60fps
        _position = position;
        _minDistance = Math.Max(0.1f, minDistance);
        _maxDistance = Math.Max(_minDistance + 0.1f, maxDistance);

        Bass.ChannelSet3DAttributes(StreamHandle, _3dMode, _minDistance, _maxDistance,
            (int)_innerAngleDegrees, (int)_outerAngleDegrees, _outerVolume);
        Bass.ChannelSet3DPosition(StreamHandle, To3DVector(_position), To3DVector(_orientation), To3DVector(_velocity));
        Bass.Apply3D();
    }

    /// <summary>
    /// Sets the 3D orientation direction of the audio source.
    /// </summary>
    /// <param name="orientation">The orientation vector (will be normalized).</param>
    internal void Set3DOrientation(Vector3 orientation)
    {
        _orientation = Vector3.Normalize(orientation);
        Bass.ChannelSet3DPosition(StreamHandle, To3DVector(_position), To3DVector(_orientation), To3DVector(_velocity));
        Bass.Apply3D();
    }

    /// <summary>
    /// Sets the 3D sound cone parameters for directional audio.
    /// </summary>
    /// <param name="innerAngleDegrees">The inner cone angle in degrees (0-360).</param>
    /// <param name="outerAngleDegrees">The outer cone angle in degrees (0-360).</param>
    /// <param name="outerVolume">The volume level outside the outer cone (0.0 to 1.0).</param>
    internal void Set3DCone(float innerAngleDegrees, float outerAngleDegrees, float outerVolume)
    {
        _innerAngleDegrees = Math.Clamp(innerAngleDegrees, 0f, 360f);
        _outerAngleDegrees = Math.Clamp(outerAngleDegrees, 0f, 360f);
        _outerVolume = Math.Clamp(outerVolume, 0f, 1f);

        Bass.ChannelSet3DAttributes(StreamHandle, _3dMode, _minDistance, _maxDistance,
            (int)_innerAngleDegrees, (int)_outerAngleDegrees, _outerVolume);
        Bass.Apply3D();
    }

    /// <summary>
    /// Sets the 3D processing mode for the audio source.
    /// </summary>
    /// <param name="mode">The 3D mode to use.</param>
    internal void Set3DMode(Mode3D mode)
    {
        _3dMode = mode;
        Bass.ChannelSet3DAttributes(StreamHandle, _3dMode, _minDistance, _maxDistance,
            (int)_innerAngleDegrees, (int)_outerAngleDegrees, _outerVolume);
        Bass.Apply3D();
    }

    /// <inheritdoc />
    internal override void Play()
    {
        base.Play();
        Bass.Apply3D();
    }

    /// <inheritdoc />
    internal override void Resume()
    {
        base.Resume();
        Bass.Apply3D();
    }

    /// <inheritdoc />
    internal override void RestartAfterExport()
    {
        base.RestartAfterExport();
        Bass.Apply3D();
    }

    /// <inheritdoc />
    internal override void PrepareForExport()
    {
        base.PrepareForExport();
        Bass.Apply3D();
    }

    /// <inheritdoc />
    protected override int GetNativeChannelCount() => CachedChannels > 0 ? CachedChannels : 1;
}
