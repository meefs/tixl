#nullable enable
using System.IO;
using T3.Core.Animation;
using T3.Core.Audio;
using T3.Core.DataTypes;
using T3.Core.DataTypes.Vector;
using T3.Core.Utils;
using T3.Editor.Gui.Interaction;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Gui.Windows.Output;
using T3.Editor.Gui.Windows.RenderExport.MF;
using T3.Editor.UiModel.ProjectHandling;

namespace T3.Editor.Gui.Windows.RenderExport;

internal static class RenderProcess
{
    public static string LastHelpString { get; private set; } = string.Empty;

    public static double Progress => _frameCount <= 1 ? 0.0 : (_frameIndex / (double)(_frameCount - 1));
    
    public static Type? MainOutputType { get; private set; }
    public static Int2 MainOutputOriginalSize;
    public static Int2 MainOutputRenderedSize;
    public static Texture2D? MainOutputTexture;
    
    public static States State;

    // TODO: clarify the difference
    public static bool IsExporting { get; private set; }
    public static bool IsToollRenderingSomething { get; private set; }
    
    public static double ExportStartedTimeLocal;
    
    public enum States
    {
        NoOutputWindow,
        NoValidOutputType,
        NoValidOutputTexture,
        WaitingForExport,
        Exporting,
    }

    /// <remarks>
    /// needs to be called once per frame
    /// </remarks>
    public static void Update()
    {
        if (!OutputWindow.TryGetPrimaryOutputWindow(out var outputWindow))
        {
            State = States.NoOutputWindow;
            return;
        }

        MainOutputTexture = outputWindow.GetCurrentTexture();
        if (MainOutputTexture == null)
        {
            State = States.NoValidOutputTexture;
            return;
        }

        MainOutputType = outputWindow.ShownInstance?.Outputs.FirstOrDefault()?.ValueType;
        if (MainOutputType != typeof(Texture2D))
        {
            State = States.NoValidOutputType;
            return;
        }

        HandleRenderShortCuts();

        if (!IsExporting)
        {
            var desc = MainOutputTexture.Description;
            MainOutputOriginalSize.Width = desc.Width;
            MainOutputOriginalSize.Height = desc.Height;

            MainOutputRenderedSize = new Int2(((int)(desc.Width * RenderSettings.Current.ResolutionFactor)).Clamp(1,16384),
                                              ((int)(desc.Height * RenderSettings.Current.ResolutionFactor)).Clamp(1,16384));
            
            State = States.WaitingForExport;
            return;
        }

        State = States.Exporting;

        // Process frame
        bool success;
        if (_renderSettings.RenderMode == RenderSettings.RenderModes.Video)
        {
            // Use the new full mixdown buffer for audio export
            double localFxTime = _frameIndex / _renderSettings.Fps;
            var audioFrameFloat = AudioRendering.GetFullMixDownBuffer(1.0 / _renderSettings.Fps, localFxTime);
            // Safety: ensure audioFrameFloat is valid and sized
            if (audioFrameFloat == null || audioFrameFloat.Length == 0)
            {
                Log.Error($"RenderProcess: AudioRendering.GetFullMixDownBuffer returned null or empty at frame {_frameIndex}", typeof(RenderProcess));
                int sampleRate = RenderAudioInfo.SoundtrackSampleRate();
                int channels = RenderAudioInfo.SoundtrackChannels();
                int floatCount = (int)Math.Max(Math.Round((1.0 / _renderSettings.Fps) * sampleRate), 0.0) * channels;
                audioFrameFloat = new float[floatCount]; // silence
            }
            // Convert float[] to byte[] for the writer
            var audioFrame = new byte[audioFrameFloat.Length * sizeof(float)];
            Buffer.BlockCopy(audioFrameFloat, 0, audioFrame, 0, audioFrame.Length);
            // Force metering outputs to update for UI/graph
            AudioRendering.EvaluateAllAudioMeteringOutputs(localFxTime);
            success = SaveVideoFrameAndAdvance(ref audioFrame, RenderAudioInfo.SoundtrackChannels(), RenderAudioInfo.SoundtrackSampleRate());
        }
        else
        {
            AudioRendering.GetLastMixDownBuffer(Playback.LastFrameDuration);
            // For image export, also update metering for UI/graph
            // Use FxTimeInBars as a substitute for LocalFxTime
            AudioRendering.EvaluateAllAudioMeteringOutputs(Playback.Current.FxTimeInBars);
            success = SaveImageFrameAndAdvance();
        }

        // Update stats
        var effectiveFrameCount = _renderSettings.RenderMode == RenderSettings.RenderModes.Video ? _frameCount : _frameCount + 2;
        var currentFrame = _renderSettings.RenderMode == RenderSettings.RenderModes.Video ? GetRealFrame() : _frameIndex + 1;

        var completed = currentFrame >= effectiveFrameCount || !success;
        if (!completed) 
            return;

        var duration = Playback.RunTimeInSecs - _exportStartedTime;
        var successful = success ? "successfully" : "unsuccessfully";
        LastHelpString = $"Render {GetTargetFilePath(_renderSettings.RenderMode)} finished {successful} in {StringUtils.HumanReadableDurationFromSeconds(duration)}";
        Log.Debug(LastHelpString);

        if (_renderSettings.AutoIncrementVersionNumber && success && _renderSettings.RenderMode == RenderSettings.RenderModes.Video)
            RenderPaths.TryIncrementVideoFileNameInUserSettings();

        Cleanup();
        IsToollRenderingSomething = false;
    }
    
    private static void HandleRenderShortCuts()
    {
        if (MainOutputTexture == null)
            return;

        if (UserActions.RenderAnimation.Triggered())
        {
            if (IsExporting)
            {
                Cancel();
            }
            else
            {
                TryStart(RenderSettings.Current);
            }
        }

        if (UserActions.RenderScreenshot.Triggered())
        {
            TryRenderScreenShot();
        }
    }

    
    public static void TryStart(RenderSettings renderSettings)
    {
        if (IsExporting)
        {
            Log.Warning("Export is already in progress");
            return;
        }
        
        var targetFilePath = GetTargetFilePath(renderSettings.RenderMode);
        if (!RenderPaths.ValidateOrCreateTargetFolder(targetFilePath))
            return;

        // Pre-check: If file exists, try to open for write to detect lock
        if (renderSettings.RenderMode == RenderSettings.RenderModes.Video && File.Exists(targetFilePath))
        {
            try
            {
                using (var fs = new FileStream(targetFilePath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    // File is not locked, can proceed
                }
            }
            catch (IOException)
            {
                var msg = $"The output file '{targetFilePath}' is currently in use by another process. Please close any application using it and try again.";
                Log.Error(msg, typeof(RenderProcess));
                LastHelpString = msg;
                return;
            }
        }

        renderSettings.FrameCount = RenderTiming.ComputeFrameCount(renderSettings);
        
        IsToollRenderingSomething = true;
        ExportStartedTimeLocal = Core.Animation.Playback.RunTimeInSecs;

        _renderSettings = renderSettings;
        
        _frameIndex = 0;
        _frameCount = Math.Max(_renderSettings.FrameCount, 0);

        _exportStartedTime = Playback.RunTimeInSecs;

        if (_renderSettings.RenderMode == RenderSettings.RenderModes.Video)
        {
            // Log all relevant parameters before initializing video writer
            Log.Debug($"Initializing Mp4VideoWriter with: path={targetFilePath}, size={MainOutputOriginalSize.Width}x{MainOutputOriginalSize.Height}, bitrate={_renderSettings.Bitrate}, framerate={_renderSettings.Fps}, audio={_renderSettings.ExportAudio}, channels={RenderAudioInfo.SoundtrackChannels()}, sampleRate={RenderAudioInfo.SoundtrackSampleRate()}");
            try
            {
                _videoWriter = new Mp4VideoWriter(targetFilePath, MainOutputOriginalSize, _renderSettings.ExportAudio)
                {
                    Bitrate = _renderSettings.Bitrate,
                    Framerate = (int)renderSettings.Fps
                };
            }
            catch (Exception ex)
            {
                var msg = $"Failed to initialize Mp4VideoWriter: {ex.Message}\n{ex.StackTrace}";
                Log.Error(msg, typeof(RenderProcess));
                LastHelpString = msg;
                Cleanup();
                IsToollRenderingSomething = false;
                return;
            }
        }
        else
        {
            _targetFolder = targetFilePath;
        }

        ScreenshotWriter.ClearQueue();

        // set playback to the first frame
        RenderTiming.SetPlaybackTimeForFrame(ref _renderSettings, _frameIndex, _frameCount, ref _runtime);
        IsExporting = true;
        LastHelpString = "Rendering…";
    }

    private static int GetRealFrame() => _frameIndex - MfVideoWriter.SkipImages;
    
    
    private static string GetTargetFilePath(RenderSettings.RenderModes renderMode)
    {
        return renderMode == RenderSettings.RenderModes.Video
                   ? RenderPaths.ResolveProjectRelativePath(UserSettings.Config.RenderVideoFilePath)
                   : RenderPaths.ResolveProjectRelativePath(UserSettings.Config.RenderSequenceFilePath);
    }

    public static void Cancel(string? reason = null)
    {
        var duration = Playback.RunTimeInSecs - _exportStartedTime;
        LastHelpString = reason ?? $"Render cancelled after {StringUtils.HumanReadableDurationFromSeconds(duration)}";
        Cleanup();
        IsToollRenderingSomething = false;
    }

    private static void Cleanup()
    {
        IsExporting = false;

        if (_renderSettings.RenderMode == RenderSettings.RenderModes.Video)
        {
            _videoWriter?.Dispose();
            _videoWriter = null;
            // Clean up operator decode streams after export
            T3.Core.Audio.AudioRendering.CleanupExportOperatorStreams();
        }

        RenderTiming.ReleasePlaybackTime(ref _renderSettings, ref _runtime);
    }

    private static bool SaveVideoFrameAndAdvance(ref byte[] audioFrame, int channels, int sampleRate)
    {
        if (Playback.OpNotReady)
        {
            Log.Debug("Waiting for operators to complete");
            return true;
        }

        try
        {
            Log.Debug($"SaveVideoFrameAndAdvance: frame={_frameIndex}, MainOutputTexture null? {MainOutputTexture == null}, audioFrame.Length={audioFrame?.Length}, channels={channels}, sampleRate={sampleRate}");
            if (MainOutputTexture == null)
            {
                Log.Error($"MainOutputTexture is null at frame {_frameIndex}", typeof(RenderProcess));
                LastHelpString = $"MainOutputTexture is null at frame {_frameIndex}";
                Cleanup();
                return false;
            }
            _videoWriter?.ProcessFrames(MainOutputTexture, ref audioFrame, channels, sampleRate);
            _frameIndex++;
            RenderTiming.SetPlaybackTimeForFrame(ref _renderSettings, _frameIndex, _frameCount, ref _runtime);
            return true;
        }
        catch (Exception e)
        {
            var msg = $"Exception in SaveVideoFrameAndAdvance at frame {_frameIndex}: {e.Message}\n{e.StackTrace}";
            Log.Error(msg, typeof(RenderProcess));
            LastHelpString = msg;
            Cleanup();
            return false;
        }
    }

    private static string GetSequenceFilePath()
    {
        var prefix = RenderPaths.SanitizeFilename(UserSettings.Config.RenderSequenceFileName);
        return Path.Combine(_targetFolder, $"{prefix}_{_frameIndex:0000}.{_renderSettings.FileFormat.ToString().ToLower()}");
    }

    private static bool SaveImageFrameAndAdvance()
    {
        if (MainOutputTexture == null)
            return false;
        
        try
        {
            var success = ScreenshotWriter.StartSavingToFile(MainOutputTexture, GetSequenceFilePath(), _renderSettings.FileFormat);
            _frameIndex++;
            RenderTiming.SetPlaybackTimeForFrame(ref _renderSettings, _frameIndex, _frameCount, ref _runtime);
            return success;
        }
        catch (Exception e)
        {
            LastHelpString = e.ToString();
            IsExporting = false;
            return false;
        }
    }

    // State
    private static Mp4VideoWriter? _videoWriter;
    private static string _targetFolder = string.Empty;
    private static double _exportStartedTime;
    private static int _frameIndex;
    private static int _frameCount;
    

    private static RenderSettings _renderSettings = null!;
    private static RenderTiming.Runtime _runtime;

    public static void TryRenderScreenShot()
    {
        if (MainOutputTexture == null) return;
        
        var project = ProjectView.Focused?.OpenedProject;
        if (project == null) return;
        
        var projectFolder = project.Package.Folder;
        var folder = Path.Combine(projectFolder, "Screenshots");            
            
        if (!Directory.Exists(folder))
            Directory.CreateDirectory(folder);

        var filename = Path.Join(folder, $"{DateTime.Now:yyyy_MM_dd-HH_mm_ss_fff}.png");
        ScreenshotWriter.StartSavingToFile(RenderProcess.MainOutputTexture, filename, ScreenshotWriter.FileFormats.Png);
        Log.Debug("Screenshot saved in: " + folder);
    }
}