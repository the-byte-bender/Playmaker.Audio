// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using OpenTK.Audio.OpenAL;

namespace Playmaker.Audio;

public sealed class AudioDevice : IDisposable
{
    private AudioEngine _engine;

    internal ALDevice RawDevice;
    internal ALContext RawContext;
    public AudioDeviceSettings Settings { get; private set; }
    public bool IsDisposed { get; private set; }

    internal AudioDevice(AudioEngine engine, AudioDeviceSettings settings)
    {
        _engine = engine;
        Settings = settings;
        var device = ALC.OpenDevice(settings.DeviceName);
        if (device == ALDevice.Null)
        {
            Utils.ALCCheckError(device);
            throw new InvalidOperationException($"Failed to open audio device: {settings.DeviceName}");
        }

        var context = ALC.CreateContext(device, settings.BuildAttributeList(device));
        if (context == ALContext.Null)
        {
            Utils.ALCCheckError(device);
            ALC.CloseDevice(device);
            throw new InvalidOperationException($"Failed to create audio context for device: {settings.DeviceName}");
        }
        RawDevice = device;
        RawContext = context;
    }

    public bool Reset(AudioDeviceSettings newSettings)
    {
        ThrowIfDisposed();

        var oldSettings = Settings;
        bool result;
        if (newSettings.DeviceName == oldSettings.DeviceName)
        {
            result = Utils.ResetDevice(RawDevice, newSettings.BuildAttributeList(RawDevice));
        }
        else
        {
            result = Utils.ReopenDevice(RawDevice, newSettings.DeviceName, newSettings.BuildAttributeList(RawDevice));
        }

        if (result)
        {
            Settings = newSettings;
        }
        return result;
    }

    private void ThrowIfDisposed()
    {
        if (IsDisposed) throw new ObjectDisposedException(nameof(AudioDevice));
    }

    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    ~AudioDevice()
    {
        Dispose(false);
    }

    private void Dispose(bool disposing)
    {
        if (IsDisposed) return;
        try
        {
            if (RawContext != ALContext.Null)
            {
                if (ALC.GetCurrentContext() == RawContext)
                {
                    ALC.MakeContextCurrent(ALContext.Null);
                }
                ALC.DestroyContext(RawContext);
                RawContext = ALContext.Null;
            }
            if (RawDevice != ALDevice.Null)
            {
                ALC.CloseDevice(RawDevice);
                RawDevice = ALDevice.Null;
            }
        }
        finally
        {
            IsDisposed = true;
        }
    }

    private const int ALC_ALL_DEVICES_SPECIFIER = 0x1013;

    /// <summary>List available playback device names.</summary>
    public static string[] GetPlaybackDevices()
    {
        var all = ALC.GetStringList((GetEnumerationStringList)ALC_ALL_DEVICES_SPECIFIER).ToArray();
        if (all.Length > 0) return all;
        return ALC.GetStringList(GetEnumerationStringList.DeviceSpecifier).ToArray();
    }

    internal void MakeCurrent()
    {
        ThrowIfDisposed();
        ALC.MakeContextCurrent(RawContext);
    }
}

/// <summary>
/// Settings used when opening or configuring an audio device.
/// </summary>
/// <param name="DeviceName">Optional device name to open; when null the system default device is used.</param>
/// <param name="Hrtf">If true, request HRTF; if false, request HRTF be disabled; if null, leave HRTF unchanged.</param>
/// <param name="HrtfId">Optional numeric HRTF index to select a specific HRTF profile.</param>
/// <param name="HrtfSpecifier">Optional HRTF specifier string to select an HRTF by name.</param>
/// <param name="RequireHrtf">When true, procedures that apply these settings will throw if the requested HRTF cannot be applied.</param>
public readonly record struct AudioDeviceSettings(
    string? DeviceName = null,
    bool? Hrtf = null,
    int? HrtfId = null,
    string? HrtfSpecifier = null,
    bool RequireHrtf = false
)
{
    internal int[] BuildAttributeList(ALDevice device)
    {
        var list = new List<int>(8);

        if (Hrtf.HasValue)
        {
            list.Add(Utils.ALC_HRTF_SOFT);
            list.Add(Hrtf.Value ? Utils.ALC_TRUE : Utils.ALC_FALSE);
        }

        int? resolvedId = HrtfId;
        if (!resolvedId.HasValue && !string.IsNullOrWhiteSpace(HrtfSpecifier))
        {
            try
            {
                if (Utils.HasHrtfExtension(device))
                {
                    var specs = Utils.GetAllHrtfSpecifiers(device);
                    for (int i = 0; i < specs.Length; i++)
                    {
                        if (string.Equals(specs[i], HrtfSpecifier, StringComparison.OrdinalIgnoreCase))
                        {
                            resolvedId = i;
                            break;
                        }
                    }
                }
            }
            catch
            {
            }
            if (!resolvedId.HasValue && RequireHrtf)
                throw new InvalidOperationException($"Requested HRTF specifier '{HrtfSpecifier}' not found.");
        }

        if (resolvedId.HasValue)
        {
            list.Add(Utils.ALC_HRTF_ID_SOFT);
            list.Add(resolvedId.Value);
        }

        list.Add(0);
        return list.ToArray();
    }
}
