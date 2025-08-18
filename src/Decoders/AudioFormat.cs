// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using OpenTK.Audio.OpenAL;

namespace Playmaker.Audio.Decoders;

/// <summary>
/// Defines the standard format for raw audio audio data used throughout the engine.
/// </summary>
/// <param name="Channels"> The number of channels (1 for mono, 2 for stereo, etc.).</param>
/// <param name="SampleRate"> The number of samples per second.</param>
/// <param name="BitsPerSample">The number of bits per sample.</param>
/// <param name="encoding">The audio encoding format.</param>
public readonly record struct AudioFormat(int Channels, int SampleRate, int BitsPerSample, AudioEncoding Encoding)
{
    internal ALFormat GetALFormat()
    {
        return Encoding switch
        {
            AudioEncoding.PCM => BitsPerSample switch
            {
                8 => Channels switch
                {
                    1 => ALFormat.Mono8,
                    2 => ALFormat.Stereo8,
                    _ => throw new ArgumentOutOfRangeException(nameof(Channels), "Unsupported channel count for 8-bit audio.")
                },
                16 => Channels switch
                {
                    1 => ALFormat.Mono16,
                    2 => ALFormat.Stereo16,
                    _ => throw new ArgumentOutOfRangeException(nameof(Channels), "Unsupported channel count for 16-bit audio.")
                },
                _ => throw new ArgumentOutOfRangeException(nameof(BitsPerSample), "Unsupported PCM bits per sample.")
            },
            AudioEncoding.Float => BitsPerSample switch
            {
                32 => Channels switch
                {
                    1 => ALFormat.MonoFloat32Ext,
                    2 => ALFormat.StereoFloat32Ext,
                    _ => throw new ArgumentOutOfRangeException(nameof(Channels), "Unsupported channel count for 32-bit float audio.")
                },
                _ => throw new ArgumentOutOfRangeException(nameof(BitsPerSample), "Unsupported Float bits per sample.")
            },
            _ => throw new ArgumentOutOfRangeException(nameof(Encoding), "Unsupported audio encoding.")
        };
    }
}


public enum AudioEncoding
{
    PCM,
    Float
}
