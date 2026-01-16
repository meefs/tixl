using System;
using System.Collections.Generic;
using System.IO;
using ImGuiNET;
using Operators.Utils;
using T3.Core.Audio;
using T3.Core.IO;
using T3.Core.UserData;
using T3.Core.Utils;
using T3.Editor.Gui.Input;
using T3.Editor.Gui.Interaction.Keyboard;
using T3.Editor.Gui.Interaction.Midi;
using T3.Editor.Gui.Styling;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.Skills.Data;
using T3.Editor.Skills.Training;
using T3.Editor.UiModel.Helpers;

namespace T3.Editor.Gui.Windows;

internal sealed partial class SettingsWindow : Window
{
    private bool? _showAdvancedAudioSettings;

    // Audio level meter smoothing
    private static float _smoothedGlobalLevel = 0f;
    private static float _smoothedOperatorLevel = 0f;
    private static float _smoothedSoundtrackLevel = 0f;

    private void DrawAudioPanel(ref bool changed)
    {
        FormInputs.AddSectionHeader("Audio System");
        FormInputs.AddVerticalSpace();
        
        // Global Mixer - compact version with volume and mute
        changed |= DrawMixerSection(
            "Global Mixer",
            "Volume",
            ref ProjectSettings.Config.GlobalPlaybackVolume,
            0.0f, 1.0f,
            ProjectSettings.Defaults.GlobalPlaybackVolume,
            "Affects all audio output at the global mixer level.",
            AudioMixerManager.GetGlobalMixerLevel(),
            ref _smoothedGlobalLevel,
            ref ProjectSettings.Config.GlobalMute,
            ProjectSettings.Defaults.GlobalMute,
            "Mute all audio output at the global mixer level."
        );
        
        // Operator Mixer - minimal version with just level meter
        DrawMixerSectionMinimal(
            "Operator Mixer",
            AudioMixerManager.GetOperatorMixerLevel(),
            ref _smoothedOperatorLevel
        );
        
        // Soundtrack Mixer - compact version with volume and mute
        changed |= DrawMixerSection(
            "Soundtrack Mixer",
            "Volume",
            ref ProjectSettings.Config.SoundtrackPlaybackVolume,
            0.0f, 10f,
            ProjectSettings.Defaults.SoundtrackPlaybackVolume,
            "Limit the audio playback volume for the soundtrack",
            AudioMixerManager.GetSoundtrackMixerLevel(),
            ref _smoothedSoundtrackLevel,
            ref ProjectSettings.Config.SoundtrackMute,
            ProjectSettings.Defaults.SoundtrackMute,
            "Mute soundtrack audio only."
        );
        
        FormInputs.AddVerticalSpace();
        FormInputs.SetIndentToLeft();
        FormInputs.AddVerticalSpace();
        
#if DEBUG
        CustomComponents.SeparatorLine();
        FormInputs.AddSectionSubHeader("Advanced Settings");
        FormInputs.SetIndentToLeft();
        
        if (!_showAdvancedAudioSettings.HasValue)
            _showAdvancedAudioSettings = false;
        var showAdvanced = _showAdvancedAudioSettings.Value;
        changed |= FormInputs.AddCheckBox("Show Advanced Audio Settings",
                                          ref showAdvanced,
                                          "Shows advanced audio configuration options. Changes to these settings require a restart.",
                                          false);
        _showAdvancedAudioSettings = showAdvanced;
        if (showAdvanced)
        {
            FormInputs.AddVerticalSpace();
            FormInputs.SetIndentToParameters();
            CustomComponents.HelpText("⚠ Warning: Changes to these settings require a restart of the application.");
            FormInputs.AddVerticalSpace();
            
            FormInputs.AddSectionSubHeader("Mixer Configuration");
            changed |= FormInputs.AddInt("Sample Rate (Hz)",
                                         ref UserSettings.Config.AudioMixerFrequency,
                                         8000, 192000, 1f,
                                         "Sample rate for all mixer streams. Common values: 44100, 48000, 96000",
                                         UserSettings.Defaults.AudioMixerFrequency);
            changed |= FormInputs.AddInt("Update Period (ms)",
                                         ref UserSettings.Config.AudioUpdatePeriodMs,
                                         1, 100, 0.1f,
                                         "BASS update period in milliseconds for low-latency playback. Lower values reduce latency but increase CPU usage.",
                                         UserSettings.Defaults.AudioUpdatePeriodMs);
            changed |= FormInputs.AddInt("Update Threads",
                                         ref UserSettings.Config.AudioUpdateThreads,
                                         1, 8, 0.1f,
                                         "Number of BASS update threads",
                                         UserSettings.Defaults.AudioUpdateThreads);
            changed |= FormInputs.AddInt("Playback Buffer Length (ms)",
                                         ref UserSettings.Config.AudioPlaybackBufferLengthMs,
                                         10, 1000, 1f,
                                         "Playback buffer length in milliseconds",
                                         UserSettings.Defaults.AudioPlaybackBufferLengthMs);
            changed |= FormInputs.AddInt("Device Buffer Length (ms)",
                                         ref UserSettings.Config.AudioDeviceBufferLengthMs,
                                         5, 500, 1f,
                                         "Device buffer length in milliseconds for low-latency output",
                                         UserSettings.Defaults.AudioDeviceBufferLengthMs);
            
            FormInputs.AddVerticalSpace();
            FormInputs.AddSectionSubHeader("FFT and Analysis");
            changed |= FormInputs.AddInt("FFT Buffer Size",
                                         ref UserSettings.Config.AudioFftBufferSize,
                                         256, 8192, 1f,
                                         "FFT buffer size for frequency analysis. Must be a power of 2.",
                                         UserSettings.Defaults.AudioFftBufferSize);
            changed |= FormInputs.AddInt("Frequency Band Count",
                                         ref UserSettings.Config.AudioFrequencyBandCount,
                                         8, 128, 1f,
                                         "Number of frequency bands for audio analysis",
                                         UserSettings.Defaults.AudioFrequencyBandCount);
            changed |= FormInputs.AddInt("Waveform Sample Count",
                                         ref UserSettings.Config.AudioWaveformSampleCount,
                                         256, 8192, 1f,
                                         "Waveform sample buffer size",
                                         UserSettings.Defaults.AudioWaveformSampleCount);
            changed |= FormInputs.AddFloat("Low-Pass Cutoff Frequency (Hz)",
                                           ref UserSettings.Config.AudioLowPassCutoffFrequency,
                                           20f, 1000f, 1f, true, true,
                                           "Low-pass filter cutoff frequency for low frequency separation",
                                           UserSettings.Defaults.AudioLowPassCutoffFrequency);
            changed |= FormInputs.AddFloat("High-Pass Cutoff Frequency (Hz)",
                                           ref UserSettings.Config.AudioHighPassCutoffFrequency,
                                           1000f, 20000f, 1f, true, true,
                                           "High-pass filter cutoff frequency for high frequency separation",
                                           UserSettings.Defaults.AudioHighPassCutoffFrequency);
        }
#endif
    }
    
    /// <summary>
    /// Draws a compact mixer section with volume, level meter, and mute controls
    /// </summary>
    private static bool DrawMixerSection(
        string sectionLabel,
        string volumeLabel,
        ref float volume,
        float minVolume,
        float maxVolume,
        float defaultVolume,
        string volumeTooltip,
        float currentLevel,
        ref float smoothedLevel,
        ref bool mute,
        bool defaultMute,
        string muteTooltip)
    {
        var changed = false;
        
        ImGui.PushID(sectionLabel);
        
        // Section header - aligned to left with minimal padding
        FormInputs.AddVerticalSpace(3);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        var leftPadding = 5 * T3Ui.UiScaleFactor;
        ImGui.SetCursorPosX(leftPadding);
        ImGui.TextUnformatted(sectionLabel.ToUpperInvariant());
        ImGui.PopStyleColor();
        ImGui.PopFont();
        FormInputs.AddVerticalSpace(2);
        
        // Volume slider and Mute checkbox on same line
        ImGui.SetCursorPosX(leftPadding);
        ImGui.AlignTextToFramePadding();
        ImGui.Text(volumeLabel);
        ImGui.SameLine();
        
        var sliderWidth = 80 * T3Ui.UiScaleFactor;
        var spacing = 10 * T3Ui.UiScaleFactor;
        ImGui.PushStyleVar(ImGuiStyleVar.FrameRounding, 5);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundInputField.Rgba);
        ImGui.SetNextItemWidth(sliderWidth);
        var volumeChanged = ImGui.DragFloat("##volume", ref volume, 0.01f, minVolume, maxVolume, "%.2f");
        ImGui.PopStyleColor();
        ImGui.PopStyleVar();
        
        // Clamp the value
        if (volume < minVolume) volume = minVolume;
        if (volume > maxVolume) volume = maxVolume;
        
        if (volumeChanged)
            changed = true;
        
        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(volumeTooltip))
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(volumeTooltip);
            ImGui.EndTooltip();
        }
        
        // Mute checkbox on same line with spacing
        ImGui.SameLine(0, spacing);
        ImGui.PushStyleColor(ImGuiCol.FrameBg, UiColors.BackgroundButton.Rgba);
        var muteChanged = ImGui.Checkbox("Mute", ref mute);
        ImGui.PopStyleColor();
        
        if (muteChanged)
            changed = true;
        
        if (ImGui.IsItemHovered() && !string.IsNullOrEmpty(muteTooltip))
        {
            ImGui.BeginTooltip();
            ImGui.TextUnformatted(muteTooltip);
            ImGui.EndTooltip();
        }
        
        // Use the standard audio level meter
        DrawAudioLevelMeter("", currentLevel, ref smoothedLevel);
        
        // Section separator
        FormInputs.AddVerticalSpace(6);
        var p = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X - 10 * T3Ui.UiScaleFactor;
        ImGui.GetWindowDrawList().AddRectFilled(
            p + new Vector2(leftPadding, 0),
            p + new Vector2(leftPadding + width, 1),
            UiColors.ForegroundFull.Fade(0.05f));
        FormInputs.AddVerticalSpace(6);
        
        ImGui.PopID();
        
        return changed;
    }
    
    /// <summary>
    /// Draws a minimal mixer section with just a level meter
    /// </summary>
    private static void DrawMixerSectionMinimal(
        string sectionLabel,
        float currentLevel,
        ref float smoothedLevel)
    {
        ImGui.PushID(sectionLabel);
        
        var leftPadding = 5 * T3Ui.UiScaleFactor;
        
        // Section header - aligned to left with minimal padding
        FormInputs.AddVerticalSpace(3);
        ImGui.PushFont(Fonts.FontSmall);
        ImGui.PushStyleColor(ImGuiCol.Text, UiColors.TextMuted.Rgba);
        ImGui.SetCursorPosX(leftPadding);
        ImGui.TextUnformatted(sectionLabel.ToUpperInvariant());
        ImGui.PopStyleColor();
        ImGui.PopFont();
        FormInputs.AddVerticalSpace(2);
        
        // Use the standard audio level meter
        DrawAudioLevelMeter("", currentLevel, ref smoothedLevel);
        
        // Section separator
        FormInputs.AddVerticalSpace(6);
        var p = ImGui.GetCursorScreenPos();
        var width = ImGui.GetContentRegionAvail().X - 10 * T3Ui.UiScaleFactor;
        ImGui.GetWindowDrawList().AddRectFilled(
            p + new Vector2(leftPadding, 0),
            p + new Vector2(leftPadding + width, 1),
            UiColors.ForegroundFull.Fade(0.05f));
        FormInputs.AddVerticalSpace(6);
        
        ImGui.PopID();
    }
}
