// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using System.Threading.Tasks;
using Playmaker.Audio.Decoders;
using Playmaker.Audio.Generators;
using Playmaker.Audio.Providers;

namespace Playmaker.Audio;

public sealed class AudioEngine : IDisposable
{
    private readonly AudioDevice _device;
    internal readonly AudioSourcePool SourcePool;
    public readonly AudioThreadMarshaller AudioThreadMarshaller = new();
    public readonly DecoderRegistry DecoderRegistry = new();
    public readonly AudioBus MasterBus;
    public readonly GeneratorProviderResolver GeneratorProviders = new();
    public readonly AudioListener Listener;
    private readonly List<AudioVoice> _allVoices = new();
    private readonly HashSet<AudioVoice> _physicalVoices = new();
    private readonly HashSet<AudioVoice> _virtualVoices = new();
    private readonly List<AudioVoice> _oneShotVoices = new();

    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Initializes the entire audio engine.
    /// </summary>
    /// <param name="deviceSettings">Initial configuration for the audio device</param>
    public AudioEngine(AudioDeviceSettings deviceSettings)
    {
        _device = new AudioDevice(this, deviceSettings);
        _device.MakeCurrent();
        SourcePool = new AudioSourcePool(this, 256);
        MasterBus = new AudioBus(this, "Master");
        Listener = new AudioListener(this);
        DecoderRegistry.RegisterFallback(stream => new LibsndfileDecoder(stream));
    }

    /// <summary>
    /// The main heartbeat of the audio engine. Call this once per frame from your game loop.
    /// </summary>
    public void Update(float deltaTime)
    {
        if (IsDisposed) return;

        AudioThreadMarshaller.ProcessActions();
        UpdateVoices(deltaTime);
        Listener.ApplyPending();
        ProcessVirtualization();
        CleanupFinishedVoices();
        AudioThreadMarshaller.ProcessActions();
    }

    /// <summary>
    /// lookup for an existing bus path.
    /// Returns null if any segment is missing.
    /// </summary>
    public AudioBus? GetBus(string path)
    {
        if (string.IsNullOrWhiteSpace(path)) return MasterBus;
        var segments = path.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = MasterBus;
        foreach (var seg in segments)
        {
            var child = current.FindChildByName(seg);
            if (child is null) return null;
            current = child;
        }
        return current;
    }

    public bool TryGetBus(string path, out AudioBus? bus)
    {
        if (string.IsNullOrWhiteSpace(path)) { bus = MasterBus; return true; }
        var segments = path.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
        var current = MasterBus;
        foreach (var seg in segments)
        {
            var child = current.FindChildByName(seg);
            if (child == null) { bus = null; return false; }
            current = child;
        }
        bus = current;
        return true;
    }

    /// <summary>
    /// Creates (or retrieves if already present) the bus at the given hierarchical path.
    /// Missing intermediate segments are created.
    /// </summary>
    public Task<AudioBus> CreateBusAsync(string path)
    {
        var tcs = new TaskCompletionSource<AudioBus>(TaskCreationOptions.RunContinuationsAsynchronously);
        AudioThreadMarshaller.Invoke(() =>
        {
            try
            {
                if (string.IsNullOrWhiteSpace(path))
                {
                    tcs.SetResult(MasterBus); return;
                }
                var segments = path!.TrimStart('/').Split('/', StringSplitOptions.RemoveEmptyEntries);
                var current = MasterBus;
                foreach (var seg in segments)
                {
                    var child = current.FindChildByName(seg) ?? current.CreateChild(seg);
                    current = child;
                }
                tcs.SetResult(current);
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    /// <summary>
    /// Deletes a bus path.
    /// </summary>
    public Task<bool> DeleteBusAsync(string path)
    {
        var tcs = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        AudioThreadMarshaller.Invoke(() =>
        {
            try
            {
                path = path.TrimStart('/');
                if (string.IsNullOrWhiteSpace(path))
                {
                    tcs.SetResult(false); return;
                }
                var segments = path!.Split('/', StringSplitOptions.RemoveEmptyEntries);
                if (segments.Length == 0)
                {
                    tcs.SetResult(false); return;
                }
                var current = MasterBus;
                for (int i = 0; i < segments.Length - 1; i++)
                {
                    var child = current.FindChildByName(segments[i]);
                    if (child is null)
                    {
                        tcs.SetResult(false); return;
                    }
                    current = child;
                }
                var target = segments[^1];
                tcs.SetResult(current.RemoveChild(target));
            }
            catch (Exception ex)
            {
                tcs.SetException(ex);
            }
        });
        return tcs.Task;
    }

    private void UpdateVoices(float deltaTime)
    {
        for (int i = 0; i < _allVoices.Count; i++)
        {
            _allVoices[i].Update(deltaTime);
        }
    }

    private void ProcessVirtualization()
    {
        if (_virtualVoices.Count == 0) return;
        foreach (var voice in _virtualVoices.ToArray())
        {
            if (TryGetSource(voice, out int src))
            {
                voice.MakePhysical(src);
            }
        }
    }

    private void CleanupFinishedVoices()
    {
        for (int i = _oneShotVoices.Count - 1; i >= 0; i--)
        {
            var v = _oneShotVoices[i];
            if (v.State == VoiceState.Stopped)
            {
                v.Dispose();
                _oneShotVoices.RemoveAt(i);
            }
        }
    }

    /// <summary>
    /// Creates a new audio emitter.
    /// </summary>
    public AudioEmitter CreateEmitter() => new AudioEmitter(this);

    internal void RegisterVoice(AudioVoice voice)
    {
        _allVoices.Add(voice);
        if (voice.IsOneShot) _oneShotVoices.Add(voice);
    }

    internal void UnregisterVoice(AudioVoice voice)
    {
        _allVoices.Remove(voice);
        _physicalVoices.Remove(voice);
        _virtualVoices.Remove(voice);
    }

    /// <summary>
    /// Creates a persistent voice. The caller is responsable for disposing it.
    /// </summary>
    public AudioVoice CreateVoice(AudioGeneratorBase generator, AudioBus? bus = null) => new AudioVoice(this, generator, bus ?? MasterBus, false);

    /// <summary>
    /// Asynchronously resolves a generator from a URI/path using <see cref="GeneratorProviders"/> and creates a persistent voice.
    /// Returns null if the generator cannot be resolved.
    /// </summary>
    public async Task<AudioVoice?> CreateVoiceAsync(string uriOrPath, AudioBus? bus = null)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath)) return null;
        var gen = await GeneratorProviders.CreateGeneratorAsync(uriOrPath).ConfigureAwait(false);
        if (gen is null) return null;
        return CreateVoice(gen, bus);
    }

    /// <summary>
    /// Plays a one-shot voice. Optional configurator runs on audio thread before starting.
    /// </summary>
    public void PlayOneShot(AudioGeneratorBase generator, AudioBus? bus = null, Action<AudioVoice>? configure = null)
    {
        var voice = new AudioVoice(this, generator, bus ?? MasterBus, true);
        AudioThreadMarshaller.Invoke(() =>
        {
            configure?.Invoke(voice);
            voice.PlayImmediate();
        });
    }

    /// <summary>
    /// Resolves a generator from a URI/path and plays a one-shot voice if successful.
    /// If the generator cannot be resolved, nothing happens.
    /// </summary>
    public async void PlayOneShot(string uriOrPath, AudioBus? bus = null, Action<AudioVoice>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath)) return;
        var gen = await GeneratorProviders.CreateGeneratorAsync(uriOrPath).ConfigureAwait(false);
        if (gen is null) return;
        PlayOneShot(gen, bus, configure);
    }

    internal bool TryGetSource(AudioVoice voice, out int source)
    {
        if (IsDisposed)
        {
            source = 0;
            return false;
        }
        return SourcePool.TryRent(out source);
    }

    internal void ReturnSource(AudioVoice voice, int source)
    {
        SourcePool.Return(source);
    }

    internal void OnVoiceStateChanged(AudioVoice voice, VoiceState oldState)
    {
        if (IsDisposed) return;

        switch (voice.State)
        {
            case VoiceState.PlayingPhysical:
            case VoiceState.PausedPhysical:
                _physicalVoices.Add(voice);
                _virtualVoices.Remove(voice);
                break;
            case VoiceState.PlayingVirtual:
            case VoiceState.PausedVirtual:
                _virtualVoices.Add(voice);
                _physicalVoices.Remove(voice);
                break;
            case VoiceState.Stopped:
            case VoiceState.Disposed:
                _physicalVoices.Remove(voice);
                _virtualVoices.Remove(voice);
                break;
        }
    }

    public void Dispose()
    {
        if (IsDisposed) return;
        IsDisposed = true;

        for (int i = _allVoices.Count - 1; i >= 0; i--)
        {
            _allVoices[i].DisposeImediate();
        }

        GeneratorProviders.Dispose();
        SourcePool.Dispose();
        _device.Dispose();
        GC.SuppressFinalize(this);
    }
}
