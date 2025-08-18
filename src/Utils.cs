// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using System.Runtime.InteropServices;
using OpenTK.Audio.OpenAL;

namespace Playmaker.Audio;

internal enum Spatialize
{
    Disabled = 0x0000,
    Enabled = 0x0001,
    Auto = 0x0002
}

internal static class Utils
{
    internal const int AL_REMIX_UNMATCHED_SOFT = 0x0002;
    internal const int AL_DIRECT_CHANNELS_SOFT = 0x1033;
    internal const int AL_SOURCE_SPATIALIZE_SOFT = 0x1214;
    internal const int ALC_HRTF_SOFT = 0x1992;
    internal const int ALC_HRTF_ID_SOFT = 0x1996;
    internal const int ALC_DONT_CARE_SOFT = 0x0002;
    internal const int ALC_HRTF_STATUS_SOFT = 0x1993;
    internal const int ALC_NUM_HRTF_SPECIFIERS_SOFT = 0x1994;
    internal const int ALC_HRTF_SPECIFIER_SOFT = 0x1995;
    internal const int ALC_HRTF_DISABLED_SOFT = 0x0000;
    internal const int ALC_HRTF_ENABLED_SOFT = 0x0001;
    internal const int ALC_HRTF_DENIED_SOFT = 0x0002;
    internal const int ALC_HRTF_REQUIRED_SOFT = 0x0003;
    internal const int ALC_HRTF_HEADPHONES_DETECTED_SOFT = 0x0004;
    internal const int ALC_HRTF_UNSUPPORTED_FORMAT_SOFT = 0x0005;
    internal const int ALC_TRUE = 1;
    internal const int ALC_FALSE = 0;

    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate IntPtr alcGetStringiSOFTDelegate(IntPtr device, int paramName, uint index);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool alcResetDeviceSOFTDelegate(IntPtr device, int[] attrList);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)]
    private delegate bool alcReopenDeviceSOFTDelegate(IntPtr device, string? deviceName, int[]? attrList);

    private static alcGetStringiSOFTDelegate? _alcGetStringiSOFT;
    private static alcResetDeviceSOFTDelegate? _alcResetDeviceSOFT;
    private static alcReopenDeviceSOFTDelegate? _alcReopenDeviceSOFT;
    private static bool _hrtfTriedLoad;

    private static void EnsureHrtfDelegates(ALDevice device)
    {
        if (_hrtfTriedLoad) return;
        _hrtfTriedLoad = true;
        if (!ALC.IsExtensionPresent(device, "ALC_SOFT_HRTF")) return;
        IntPtr p1 = ALC.GetProcAddress(device, "alcGetStringiSOFT");
        if (p1 != IntPtr.Zero)
            _alcGetStringiSOFT = Marshal.GetDelegateForFunctionPointer<alcGetStringiSOFTDelegate>(p1);
        IntPtr p2 = ALC.GetProcAddress(device, "alcResetDeviceSOFT");
        if (p2 != IntPtr.Zero)
            _alcResetDeviceSOFT = Marshal.GetDelegateForFunctionPointer<alcResetDeviceSOFTDelegate>(p2);
    }

    private static void EnsureReopenDelegate(ALDevice device)
    {
        if (_alcReopenDeviceSOFT != null) return;
        if (!ALC.IsExtensionPresent(device, "ALC_SOFT_reopen_device")) return;
        IntPtr p = ALC.GetProcAddress(device, "alcReopenDeviceSOFT");
        if (p != IntPtr.Zero)
            _alcReopenDeviceSOFT = Marshal.GetDelegateForFunctionPointer<alcReopenDeviceSOFTDelegate>(p);
    }

    internal static void SetDirectChannels(int source, bool value)
    {
        if (AL.IsExtensionPresent("AL_SOFT_direct_channels"))
        {
            CheckALError(false);
            AL.Source(source, (ALSourcei)AL_DIRECT_CHANNELS_SOFT, value ? AL_REMIX_UNMATCHED_SOFT : 0);
            CheckALError();
        }
        else
        {
            throw new NotSupportedException("AL_SOFT_direct_channels extension is not supported.");
        }
    }

    internal static bool GetDirectChannels(int source)
    {
        if (AL.IsExtensionPresent("AL_SOFT_direct_channels"))
        {
            CheckALError(false);
            AL.GetSource(source, (ALGetSourcei)AL_DIRECT_CHANNELS_SOFT, out var value);
            CheckALError();
            return value == AL_REMIX_UNMATCHED_SOFT;
        }
        else
        {
            throw new NotSupportedException("AL_SOFT_direct_channels extension is not supported.");
        }
    }

    internal static void SetSpatialize(int source, Spatialize value)
    {
        if (AL.IsExtensionPresent("AL_SOFT_source_spatialize"))
        {
            CheckALError(false);
            AL.Source(source, (ALSourcei)AL_SOURCE_SPATIALIZE_SOFT, (int)value);
            CheckALError();
        }
        else
        {
            throw new NotSupportedException("AL_SOFT_source_spatialize extension is not supported.");
        }
    }

    internal static Spatialize GetSpatialize(int source)
    {
        if (AL.IsExtensionPresent("AL_SOFT_source_spatialize"))
        {
            CheckALError(false);
            AL.GetSource(source, (ALGetSourcei)AL_SOURCE_SPATIALIZE_SOFT, out var value);
            CheckALError();
            return (Spatialize)value;
        }
        else
        {
            throw new NotSupportedException("AL_SOFT_source_spatialize extension is not supported.");
        }
    }

    internal static void CheckALError(bool throwOnError = true)
    {
        ALError error = AL.GetError();
        if (error != ALError.NoError && throwOnError)
        {
            throw new InvalidOperationException($"OpenAL error: {error}");
        }
    }

    internal static void ALCheckError(bool throwOnError = true) => CheckALError(throwOnError);

    internal static void ALCCheckError(ALDevice device, bool throwOnError = true)
    {
        var err = ALC.GetError(device);
        if (err != AlcError.NoError && throwOnError)
        {
            throw new InvalidOperationException($"OpenALC error: {err}");
        }
    }

    internal static string? GetHrtfSpecifier(ALDevice device, uint index)
    {
        EnsureHrtfDelegates(device);
        if (_alcGetStringiSOFT == null) return null;
        IntPtr ptr = _alcGetStringiSOFT(device.Handle, ALC_HRTF_SPECIFIER_SOFT, index);
        return ptr == IntPtr.Zero ? null : Marshal.PtrToStringAnsi(ptr);
    }

    internal static bool ResetDevice(ALDevice device, params int[] attrList)
    {
        EnsureHrtfDelegates(device);
        if (_alcResetDeviceSOFT == null) return false;
        bool ok = _alcResetDeviceSOFT(device.Handle, attrList);
        if (!ok)
        {
            ALCCheckError(device);
        }
        else
        {
            ALCCheckError(device, throwOnError: false);
        }
        return ok;
    }

    internal static bool ReopenDevice(ALDevice device, string? newDeviceName, int[]? attrList)
    {
        EnsureReopenDelegate(device);
        if (_alcReopenDeviceSOFT == null) return false;
        bool ok = _alcReopenDeviceSOFT(device.Handle, newDeviceName, attrList ?? Array.Empty<int>());
        if (!ok)
        {
            ALCCheckError(device);
        }
        else
        {
            ALCCheckError(device, throwOnError: false);
        }
        return ok;
    }

    internal static bool IsHrtfEnabled(ALDevice device)
    {
        int status;
        ALC.GetInteger(device, (AlcGetInteger)ALC_HRTF_STATUS_SOFT, 1, out status);
        ALCCheckError(device, throwOnError: false);
        return status == ALC_HRTF_ENABLED_SOFT;
    }

    internal static int GetHrtfStatus(ALDevice device)
    {
        int status;
        ALC.GetInteger(device, (AlcGetInteger)ALC_HRTF_STATUS_SOFT, 1, out status);
        ALCCheckError(device, throwOnError: false);
        return status;
    }

    internal static string? GetHrtfSpecifierString(ALDevice device)
    {
        var s = ALC.GetString(device, (AlcGetString)ALC_HRTF_SPECIFIER_SOFT);
        ALCCheckError(device, throwOnError: false);
        return s;
    }

    internal static string[] GetAllHrtfSpecifiers(ALDevice device)
    {
        int numSpecifiers;
        ALC.GetInteger(device, (AlcGetInteger)ALC_NUM_HRTF_SPECIFIERS_SOFT, 1, out numSpecifiers);
        ALCCheckError(device, throwOnError: false);
        string[] specifiers = new string[numSpecifiers];
        for (uint i = 0; i < numSpecifiers; i++)
        {
            specifiers[i] = GetHrtfSpecifier(device, i)!;
        }
        return specifiers;
    }

    internal enum HrtfStatus
    {
        Disabled = ALC_HRTF_DISABLED_SOFT,
        Enabled = ALC_HRTF_ENABLED_SOFT,
        Denied = ALC_HRTF_DENIED_SOFT,
        Required = ALC_HRTF_REQUIRED_SOFT,
        HeadphonesDetected = ALC_HRTF_HEADPHONES_DETECTED_SOFT,
        UnsupportedFormat = ALC_HRTF_UNSUPPORTED_FORMAT_SOFT
    }

    internal static HrtfStatus GetHrtfStatusEnum(ALDevice device) => (HrtfStatus)GetHrtfStatus(device);

    internal static bool HasHrtfExtension(ALDevice device) => ALC.IsExtensionPresent(device, "ALC_SOFT_HRTF");
}