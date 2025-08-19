// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using OpenTK.Audio.OpenAL;
using Playmaker.Audio.Decoders;

namespace Playmaker.Audio.Generators;

/// <summary>
/// A generator that decodes an audio stream in chunks on a background thread.
/// </summary>
public sealed class StreamingSoundGenerator : StreamingAudioGeneratorBase
{
    private readonly IDecoder _decoder;
    private Task? _decodingTask;
    private CancellationTokenSource? _cts;
    private readonly ManualResetEventSlim _pauseEvent = new(true);

    public override TimeSpan? Duration => _decoder.Duration;
    public override bool CanSeek => _decoder.CanSeek;

    public StreamingSoundGenerator(AudioEngine engine, IDecoder decoder, int bufferCount = 4)
        : base(engine, bufferCount)
    {
        _decoder = decoder;
    }

    /// <summary>
    /// Starts the background decoding thread.
    /// </summary>
    public override ValueTask InitializeAsync(CancellationToken cancellationToken = default)
    {
        _cts = new CancellationTokenSource();
        _decodingTask = Task.Run(() => DecodingLoop(_cts.Token));
        return ValueTask.CompletedTask;
    }

    private void DecodingLoop(CancellationToken token)
    {
        try
        {
            var format = _decoder.Format;
            var alFormat = format.GetALFormat();
            if (format.Channels == 0) throw new InvalidDataException("Decoder reported zero channels.");

            const int framesPerChunk = 2048;
            int bytesPerSample = format.BitsPerSample / 8;
            var decodeBuffer = new byte[framesPerChunk * format.Channels * bytesPerSample];
            var decodeSpan = decodeBuffer.AsSpan();

            while (!token.IsCancellationRequested)
            {
                _pauseEvent.Wait(token);

                if (_filledBuffers.Count < StreamBufferCount && _freeBuffers.TryPop(out int freeAlBuffer))
                {
                    int framesRead = _decoder.Decode(decodeSpan);

                    if (framesRead > 0)
                    {
                        int bytesRead = framesRead * format.Channels * bytesPerSample;
                        var dataToSend = new byte[bytesRead];
                        decodeSpan.Slice(0, bytesRead).CopyTo(dataToSend);
                        Engine.AudioThreadMarshaller.Invoke(() =>
                        {
                            AL.BufferData(freeAlBuffer, alFormat, dataToSend, format.SampleRate);
                            Utils.CheckALError();
                            _filledBuffers.Push(freeAlBuffer);
                        });
                    }
                    else
                    {
                        if (Looping && CanSeek)
                        {
                            _freeBuffers.Push(freeAlBuffer);
                            _decoder.TrySeek(TimeSpan.Zero);
                            continue;
                        }
                        _freeBuffers.Push(freeAlBuffer);
                        MarkEndOfStream();
                        break;
                    }
                }
                else
                {
                    Thread.Sleep(5);
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception ex)
        {
            Console.WriteLine($"[StreamingGenerator] Decoding loop failed: {ex}");
        }
    }

    public override void Pause() => _pauseEvent.Reset();
    public override void Resume() => _pauseEvent.Set();

    protected override void DoSeek(TimeSpan position)
    {
        lock (_decoder)
        {
            _decoder.TrySeek(position);
        }
    }

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            _cts?.Cancel();
            _cts?.Dispose();
            _decoder.Dispose();
        }
        base.Dispose(disposing);
    }
}