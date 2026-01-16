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
        FormInputs.SetIndentToParameters();
        
        // Global Mixer section
        FormInputs.AddSectionSubHeader("Global Mixer");
        changed |= FormInputs.AddFloat("Global Volume",
                                       ref ProjectSettings.Config.GlobalPlaybackVolume,
                                       0.0f, 1.0f, 0.01f, true, true,
                                       "Affects all audio output at the global mixer level.",
                                       ProjectSettings.Defaults.GlobalPlaybackVolume);

        // Global Mixer Level Meter
        DrawAudioLevelMeter("Global Level", AudioMixerManager.GetGlobalMixerLevel(), ref _smoothedGlobalLevel);

        changed |= FormInputs.AddCheckBox("Global Mute",
                                        ref ProjectSettings.Config.GlobalMute,
                                        "Mute all audio output at the global mixer level.",
                                        ProjectSettings.Defaults.GlobalMute);

        FormInputs.AddVerticalSpace();
        
        // Operator Mixer section
        FormInputs.AddSectionSubHeader("Operator Audio");
        
        // Operator Mixer Level Meter
        DrawAudioLevelMeter("Operator Level", AudioMixerManager.GetOperatorMixerLevel(), ref _smoothedOperatorLevel);

        FormInputs.AddVerticalSpace();
        
        // Soundtrack Mixer section
        FormInputs.AddSectionSubHeader("Soundtrack");
        
        changed |= FormInputs.AddFloat("Soundtrack Volume",
                                       ref ProjectSettings.Config.SoundtrackPlaybackVolume,
                                       0.0f, 10f, 0.01f, true, true,
                                       "Limit the audio playback volume for the soundtrack",
                                       ProjectSettings.Defaults.SoundtrackPlaybackVolume);

        // Soundtrack Mixer Level Meter
        DrawAudioLevelMeter("Soundtrack Level", AudioMixerManager.GetSoundtrackMixerLevel(), ref _smoothedSoundtrackLevel);

        changed |= FormInputs.AddCheckBox("Soundtrack Mute",
                                        ref ProjectSettings.Config.SoundtrackMute,
                                        "Mute soundtrack audio only.",
                                        ProjectSettings.Defaults.SoundtrackMute);
        
        FormInputs.AddVerticalSpace();
        FormInputs.SetIndentToLeft();
        FormInputs.AddVerticalSpace();
        FormInputs.AddSectionSubHeader("Advanced Settings");
        FormInputs.SetIndentToLeft();
#if DEBUG
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
}
