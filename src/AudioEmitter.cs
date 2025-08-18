// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using System;
using System.Collections.Generic;
using System.Numerics;
using Playmaker.Audio.Generators;

namespace Playmaker.Audio;

/// <summary>
/// Represents an anchor in 3D space that can emit sound.
/// Voices can be attached to an emitter to automatically follow its transform.
/// An emitter can also be attached to a bus to act as a routing point for all its voices.
/// </summary>
public sealed class AudioEmitter
{
    private readonly AudioEngine _engine;
    private Vector3 _position = Vector3.Zero;
    private Vector3 _velocity = Vector3.Zero;
    private AudioBus? _bus;
    private int _priorityBias = 0;

    internal int Version { get; private set; } = 0;
    public bool IsDisposed { get; private set; }

    /// <summary>
    /// Gets the current world-space position of the emitter.
    /// </summary>
    public Vector3 Position => _position;

    /// <summary>
    /// Gets the current world-space velocity of the emitter.
    /// </summary>
    public Vector3 Velocity => _velocity;

    /// <summary>
    /// Gets the bus this emitter is routed to. Voices attached to this emitter will inherit this bus assignment. Returns null if no bus is explicitly set.
    /// </summary>
    public AudioBus? Bus => _bus;

    /// <summary>
    /// Gets the priority bias of the emitter. This value is added to the priority of all voices attached to this emitter.
    /// </summary>
    public int PriorityBias => _priorityBias;

    internal AudioEmitter(AudioEngine engine)
    {
        _engine = engine;
    }

    /// <summary>
    /// Updates the transform of the emitter. 
    /// </summary>
    public void UpdateTransform(Vector3 position, Vector3 velocity)
        => _engine.AudioThreadMarshaller.Invoke(() =>
        {
            if (_position == position && _velocity == velocity) return;
            _position = position;
            _velocity = velocity;
            Version++;
        });

    /// <summary>
    /// Sets the bus that this emitter (and its attached voices) should be routed through.
    /// </summary>
    /// <param name="bus">The bus to route to. Can be null to clear the assignment.</param>
    public void SetBus(AudioBus? bus)
        => _engine.AudioThreadMarshaller.Invoke(() =>
        {
            if (_bus == bus) return;
            _bus = bus;
            Version++;
        });

    /// <summary>
    /// Creates a persistent voice attached to this emitter.
    /// </summary>
    public AudioVoice CreateVoice(AudioGeneratorBase generator)
    {
        var targetBus = _bus ?? _engine.MasterBus;
        var voice = new AudioVoice(_engine, generator, targetBus, false);
        _engine.AudioThreadMarshaller.Invoke(() =>
        {
            voice.AttachToInternal(this);
        });
        return voice;
    }

    /// <summary>
    /// Asynchronously resolves a generator from a URI/path and creates a persistent voice attached to this emitter.
    /// Returns null if resolution fails.
    /// </summary>
    public async Task<AudioVoice?> CreateVoiceAsync(string uriOrPath)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath)) return null;
        var gen = await _engine.GeneratorProviders.CreateGeneratorAsync(uriOrPath).ConfigureAwait(false);
        if (gen is null) return null;
        return CreateVoice(gen);
    }

    /// <summary>
    /// Plays a one-shot attached to this emitter.
    /// </summary>
    public void PlayOneShot(AudioGeneratorBase generator, Action<AudioVoice>? configure = null)
    {
        var targetBus = _bus ?? _engine.MasterBus;
        var voice = new AudioVoice(_engine, generator, targetBus, true);
        _engine.AudioThreadMarshaller.Invoke(() =>
        {
            voice.AttachToInternal(this);
            configure?.Invoke(voice);
            voice.PlayImmediate();
        });
    }

    /// <summary>
    /// Resolves a generator from a URI/path and plays a one-shot if successful.
    /// If generator cannot be resolved, does nothing.
    /// </summary>
    public async void PlayOneShot(string uriOrPath, Action<AudioVoice>? configure = null)
    {
        if (string.IsNullOrWhiteSpace(uriOrPath)) return;
        var gen = await _engine.GeneratorProviders.CreateGeneratorAsync(uriOrPath).ConfigureAwait(false);
        if (gen is null) return;
        PlayOneShot(gen, configure);
    }
}
