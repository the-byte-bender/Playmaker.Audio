// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using OpenTK.Audio.OpenAL;

namespace Playmaker.Audio.Generators;

/// <summary>
/// The abstract base class for all audio data sources.
/// Manages lifetime, reference counting, and the core contract for attaching to voices.
/// </summary>
public abstract class AudioGeneratorBase : IDisposable
{
    internal event Action<AudioGeneratorBase>? Disposed;

    protected readonly AudioEngine Engine;

    private int _refCount = 1;
    private bool _isDisposed;

    /// <summary>
    /// Gets a value indicating whether this generator is exclusive to a single voice (e.g., a stream).
    /// If false, this generator can be shared and cached.
    /// </summary>
    public abstract bool IsExclusive { get; }

    /// <summary>
    /// Gets the duration of the audio, if known. Returns null for infinite streams.
    /// </summary>
    public abstract TimeSpan? Duration { get; }

    protected AudioGeneratorBase(AudioEngine engine)
    {
        Engine = engine;
    }

    /// <summary>
    /// Asynchronously performs any heavy initialization, such as decoding a full file.
    /// </summary>
    public virtual ValueTask InitializeAsync(CancellationToken cancellationToken = default) => ValueTask.CompletedTask;

    /// <summary>
    /// Called by an AudioVoice when it begins using this generator.
    /// </summary>
    internal void AttachTo(AudioVoice voice)
    {
        Interlocked.Increment(ref _refCount);
        OnAttached(voice);
    }

    /// <summary>
    /// Override to perform implementation-specific logic when a voice attaches
    /// </summary>
    protected virtual void OnAttached(AudioVoice voice) { }

    /// <summary>
    /// Called by an AudioVoice when it stops using this generator.
    /// </summary>
    internal void DetachFrom(AudioVoice voice)
    {
        OnDetached(voice);

        if (Interlocked.Decrement(ref _refCount) <= 0)
        {
            Dispose();
        }
    }

    internal void IncrementReferenceCount()
    {
        Interlocked.Increment(ref _refCount);
    }

    internal void DecrementReferenceCount()
    {
        Interlocked.Decrement(ref _refCount);

        if (_refCount <= 0)
        {
            Dispose();
        }
    }

    internal void SilentRelease()
    {
        Interlocked.Decrement(ref _refCount);
    }

    /// <summary>
    /// Override to perform implementation-specific logic when a voice detaches.
    /// </summary>
    protected virtual void OnDetached(AudioVoice voice) { }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        Dispose(true);
        Disposed?.Invoke(this);
        GC.SuppressFinalize(this);
    }

    protected virtual void Dispose(bool disposing) { }

    ~AudioGeneratorBase()
    {
        Dispose(false);
    }
}


/// <summary>
/// Abstract base for generators that represent a single, fully-loaded, shareable audio buffer.
/// </summary>
public abstract class StaticAudioGeneratorBase : AudioGeneratorBase
{
    public readonly int RawBuffer;

    public override bool IsExclusive => false;

    protected StaticAudioGeneratorBase(AudioEngine engine) : base(engine)
    {
        RawBuffer = AL.GenBuffer();
        Utils.CheckALError();
    }

    protected override void Dispose(bool disposing)
    {
        AL.DeleteBuffer(RawBuffer);
        Utils.CheckALError();
        base.Dispose(disposing);
    }
}


/// <summary>
/// Abstract base for generators that stream audio data in chunks using a ring buffer.
/// </summary>
public abstract class StreamingAudioGeneratorBase : AudioGeneratorBase
{
    private readonly int[] _rawBuffers;
    protected readonly ConcurrentStack<int> _freeBuffers;
    protected readonly ConcurrentStack<int> _filledBuffers;
    private volatile bool _endOfStream;
    public bool Looping { get; set; }

    public override bool IsExclusive => true;
    public int StreamBufferCount { get; }
    internal bool EndOfStream => _endOfStream;

    protected StreamingAudioGeneratorBase(AudioEngine engine, int bufferCount = 4) : base(engine)
    {
        if (bufferCount < 2)
            throw new ArgumentOutOfRangeException(nameof(bufferCount), "Streaming requires at least 2 buffers.");

        StreamBufferCount = bufferCount;

        _rawBuffers = AL.GenBuffers(StreamBufferCount);
        Utils.CheckALError();
        _freeBuffers = new ConcurrentStack<int>(_rawBuffers);
        _filledBuffers = new ConcurrentStack<int>();
    }

    public abstract bool CanSeek { get; }
    public virtual void Pause() { }
    public virtual void Resume() { }

    public void Seek(TimeSpan position)
    {
        if (!CanSeek)
            throw new NotSupportedException("This audio generator does not support seeking.");

        Pause();
        _endOfStream = false;
        while (_filledBuffers.TryPop(out int buffer))
        {
            _freeBuffers.Push(buffer);
        }
        DoSeek(position);
        Resume();
    }

    /// <summary>
    /// Override to perform the low-level seek on the underlying decoder or stream.
    /// </summary>
    protected virtual void DoSeek(TimeSpan position) { }

    internal bool TryGetFilledBuffer(out int buffer)
    {
        return _filledBuffers.TryPop(out buffer);
    }

    internal void ReturnFreeBuffer(int buffer)
    {
        _freeBuffers.Push(buffer);
    }

    internal void MarkEndOfStream()
    {
        _endOfStream = true;
    }

    protected override void Dispose(bool disposing)
    {
        if (_rawBuffers.Length > 0)
        {
            AL.DeleteBuffers(_rawBuffers);
            Utils.CheckALError();
        }
        base.Dispose(disposing);
    }
}