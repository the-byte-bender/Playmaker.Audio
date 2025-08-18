// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Playmaker.Audio.Decoders;

/// <summary>
/// A decoder for various audio formats using the libsndfile library.
/// This implementation reads from a C# Stream and will take ownership of the stream.
/// </summary>
public sealed unsafe class LibsndfileDecoder : IDecoder
{
    private readonly Stream _stream;
    private IntPtr _sndfileHandle;
    private GCHandle _streamHandle;
    private bool _disposed;

    /// <inheritdoc />
    public bool CanSeek { get; }
    /// <inheritdoc />
    public AudioFormat Format { get; }
    /// <inheritdoc />
    public TimeSpan? Duration { get; }

    /// <param name="stream">The stream containing the encoded audio data. The stream must be readable and will be disposed by this class.</param>
    /// <exception cref="ArgumentException">Thrown if the stream is not readable.</exception>
    /// <exception cref="InvalidOperationException">Thrown if libsndfile fails to open or recognize the stream format.</exception>
    public LibsndfileDecoder(Stream stream)
    {
        if (!stream.CanRead) throw new ArgumentException("Stream must be readable.", nameof(stream));

        _stream = stream;
        _streamHandle = GCHandle.Alloc(_stream);

        var virtualIo = new Libsndfile.SF_VIRTUAL_IO
        {
            get_filelen = &VioGetLength,
            seek = &VioSeek,
            read = &VioRead,
            write = &VioWrite,
            tell = &VioTell
        };

        var sfInfo = new Libsndfile.SF_INFO();
        _sndfileHandle = Libsndfile.sf_open_virtual(ref virtualIo, Libsndfile.SFM_READ, ref sfInfo, GCHandle.ToIntPtr(_streamHandle));

        if (_sndfileHandle == IntPtr.Zero)
        {
            if (_streamHandle.IsAllocated)
            {
                _streamHandle.Free();
            }
            _stream.Dispose();
            string errorMessage = Libsndfile.sf_strerror(IntPtr.Zero);
            throw new InvalidOperationException($"Libsndfile failed to open virtual stream: {errorMessage}");
        }

        CanSeek = _stream.CanSeek && sfInfo.seekable != 0;
        Format = new AudioFormat(sfInfo.channels, sfInfo.samplerate, 16, AudioEncoding.PCM);
        Duration = (CanSeek && sfInfo.frames > 0 && sfInfo.samplerate > 0)
            ? TimeSpan.FromSeconds((double)sfInfo.frames / sfInfo.samplerate)
            : null;
    }

    ~LibsndfileDecoder()
    {
        Dispose(false);
    }

    /// <inheritdoc />
    public int Decode(Span<short> pcmBuffer)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LibsndfileDecoder));
        if (Format.Channels == 0) return 0;

        if (pcmBuffer.Length % Format.Channels != 0)
        {
            throw new ArgumentException("Buffer length must be a multiple of the number of channels.", nameof(pcmBuffer));
        }

        long framesToRead = pcmBuffer.Length / Format.Channels;
        long framesRead = Libsndfile.sf_readf_short(_sndfileHandle, pcmBuffer, framesToRead);

        return (int)framesRead;
    }

    /// <inheritdoc />
    public bool TrySeek(TimeSpan position)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(LibsndfileDecoder));
        if (!CanSeek) return false;

        long frameOffset = (long)(position.TotalSeconds * Format.SampleRate);
        long result = Libsndfile.sf_seek(_sndfileHandle, frameOffset, Libsndfile.SEEK_SET);

        return result != -1;
    }

    /// <inheritdoc />
    public bool TryRewind() => TrySeek(TimeSpan.Zero);

    /// <inheritdoc />
    public void Dispose()
    {
        Dispose(true);
        GC.SuppressFinalize(this);
    }

    private void Dispose(bool disposing)
    {
        if (_disposed) return;

        if (_sndfileHandle != IntPtr.Zero)
        {
            Libsndfile.sf_close(_sndfileHandle);
            _sndfileHandle = IntPtr.Zero;
        }

        if (disposing)
        {
            if (_streamHandle.IsAllocated)
            {
                _streamHandle.Free();
            }
            _stream.Dispose();
        }

        _disposed = true;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long VioGetLength(IntPtr userData)
    {
        var stream = (Stream)GCHandle.FromIntPtr(userData).Target!;
        return stream.CanSeek ? stream.Length : 0;
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long VioSeek(long offset, int whence, IntPtr userData)
    {
        var stream = (Stream)GCHandle.FromIntPtr(userData).Target!;
        if (!stream.CanSeek) return -1;

        SeekOrigin origin = whence switch
        {
            Libsndfile.SEEK_SET => SeekOrigin.Begin,
            Libsndfile.SEEK_CUR => SeekOrigin.Current,
            Libsndfile.SEEK_END => SeekOrigin.End,
            _ => SeekOrigin.Begin
        };

        return stream.Seek(offset, origin);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long VioRead(IntPtr ptr, long count, IntPtr userData)
    {
        var stream = (Stream)GCHandle.FromIntPtr(userData).Target!;
        var buffer = new Span<byte>(ptr.ToPointer(), (int)count);
        return stream.Read(buffer);
    }

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long VioWrite(IntPtr ptr, long count, IntPtr userData) => 0;

    [UnmanagedCallersOnly(CallConvs = new[] { typeof(CallConvCdecl) })]
    private static long VioTell(IntPtr userData)
    {
        var stream = (Stream)GCHandle.FromIntPtr(userData).Target!;
        return stream.CanSeek ? stream.Position : 0;
    }
}
