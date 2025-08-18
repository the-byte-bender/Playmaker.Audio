// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using OpenTK.Audio.OpenAL;
using Playmaker.Audio.Decoders;

namespace Playmaker.Audio.Generators;

/// <summary>
/// A generator that fully decodes an audio stream into a single, static OpenAL buffer.
/// </summary>
public sealed class StaticSoundGenerator : StaticAudioGeneratorBase
{
    private IDecoder _decoder;
    public override TimeSpan? Duration => _decoder.Duration;

    public StaticSoundGenerator(AudioEngine engine, IDecoder decoder) : base(engine)
    {
        _decoder = decoder;
    }

    public override async ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        using (_decoder)
        {
            using var pcmStream = new MemoryStream();

            var format = _decoder.Format;
            await Task.Run(() =>
            {
                var channels = format.Channels;
                if (channels == 0)
                {
                    throw new InvalidOperationException("Decoder format reported zero channels.");
                }
                const int framesToReadPerChunk = 4096;
                var decodeBuffer = new short[framesToReadPerChunk * channels];
                var decodeSpan = decodeBuffer.AsSpan();

                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    int framesRead = _decoder.Decode(decodeSpan);
                    if (framesRead == 0)
                    {
                        break;
                    }

                    int totalSamplesRead = framesRead * channels;
                    var validDataSpan = decodeSpan.Slice(0, totalSamplesRead);
                    var byteSpan = System.Runtime.InteropServices.MemoryMarshal.AsBytes(validDataSpan);
                    pcmStream.Write(byteSpan);
                }
            }, cancellationToken);
            _decoder = null!;

            await Engine.AudioThreadMarshaller.InvokeAsync(() =>
            {
                var alFormat = format.GetALFormat();
                ReadOnlySpan<byte> pcmData = pcmStream.GetBuffer().AsSpan(0, (int)pcmStream.Length);

                AL.BufferData(RawBuffer, alFormat, pcmData, format.SampleRate);
                Utils.CheckALError();
            });
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _decoder?.Dispose();
        }
        base.Dispose(disposing);
    }
}