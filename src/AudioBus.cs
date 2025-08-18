// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;

namespace Playmaker.Audio;

/// <summary>
/// A hierarchical mixing node that groups related sounds so their overall loudness, character, and routing can be shaped together. Buses form a tree, letting broad mix decisions flow downward while still allowing local overrides. 
/// </summary>
public class AudioBus
{
    private readonly AudioEngine _engine;
    private readonly ConcurrentDictionary<string, AudioBus> _children = new();

    private float _localGain = 1.0f;
    private float _localPitch = 1.0f;
    private int _localPriorityBias = 0;
    private bool _isMuted = false;

    public string Name { get; }
    public AudioBus? Parent { get; }

    /// <summary>
    /// Gets or sets the local gain (volume multiplier) of this bus.
    /// This value is multiplied by the parent's effective gain in the final mix.
    /// Values are clamped to be non-negative.
    /// </summary>
    public float Gain
    {
        get => _localGain;
        set
        {
            var desired = Math.Max(0.0f, value);
            _engine.AudioThreadMarshaller.Invoke(() =>
            {
                if (Math.Abs(desired - _localGain) > float.Epsilon)
                {
                    _localGain = desired;
                    MarkDirty();
                }
            });
        }
    }

    /// <summary>
    /// Gets or sets the local pitch multiplier of this bus.
    /// This value is multiplied by the parent's effective pitch in the final mix.
    /// Values are clamped to be positive.
    /// </summary>
    public float Pitch
    {
        get => _localPitch;
        set
        {
            var desired = Math.Max(0.001f, value);
            _engine.AudioThreadMarshaller.Invoke(() =>
            {
                if (Math.Abs(desired - _localPitch) > float.Epsilon)
                {
                    _localPitch = desired;
                    MarkDirty();
                }
            });
        }
    }

    /// <summary>
    /// Gets or sets the local priority bias of this bus.
    /// This value is added to the parent's effective priority in the final mix.
    /// </summary>
    public int PriorityBias
    {
        get => _localPriorityBias;
        set
        {
            var desired = value;
            _engine.AudioThreadMarshaller.Invoke(() =>
            {
                if (desired != _localPriorityBias)
                {
                    _localPriorityBias = desired;
                    MarkDirty();
                }
            });
        }
    }

    /// <summary>
    /// Gets or sets whether this bus is locally muted.
    /// In the final mix, a bus is effectively muted if it or any of its parents are muted.
    /// </summary>
    public bool IsMuted
    {
        get => _isMuted;
        set
        {
            var desired = value;
            _engine.AudioThreadMarshaller.Invoke(() =>
            {
                if (desired != _isMuted)
                {
                    _isMuted = desired;
                    MarkDirty();
                }
            });
        }
    }

    public float EffectiveGain { get; private set; } = 1.0f;
    public float EffectivePitch { get; private set; } = 1.0f;
    public int EffectivePriorityBias { get; private set; } = 0;
    public bool IsEffectivelyMuted { get; private set; } = false;
    private int _version;
    internal int Version => _version;

    internal AudioBus(AudioEngine engine, string name, AudioBus? parent = null)
    {
        Name = name;
        _engine = engine;
        Parent = parent;
    }

    internal void Recalculate(bool recalculateChildren = true)
    {
        float parentGain = Parent?.EffectiveGain ?? 1.0f;
        float parentPitch = Parent?.EffectivePitch ?? 1.0f;
        int parentPriorityBias = Parent?.EffectivePriorityBias ?? 0;
        bool parentMuted = Parent?.IsEffectivelyMuted ?? false;

        EffectiveGain = _localGain * parentGain;
        EffectivePitch = _localPitch * parentPitch;
        EffectivePriorityBias = _localPriorityBias + parentPriorityBias;
        IsEffectivelyMuted = _isMuted || parentMuted;

        if (IsEffectivelyMuted)
        {
            EffectiveGain = 0.0f;
        }

        if (recalculateChildren)
        {
            foreach (var child in _children.Values)
            {
                child.Recalculate(recalculateChildren);
            }
        }
    }

    private void MarkDirty()
    {
        Recalculate(false);
        foreach (var child in _children.Values)
        {
            child.MarkDirty();
        }
        _version++;
    }

    internal AudioBus? FindChildByName(string name)
    {
        _children.TryGetValue(name, out var child);
        return child;
    }

    internal AudioBus CreateChild(string name)
    {
        return _children.GetOrAdd(name, key =>
        {
            var newBus = new AudioBus(_engine, key, this);
            newBus.MarkDirty();
            return newBus;
        });
    }

    internal bool RemoveChild(string name)
    {
        return _children.TryRemove(name, out _);
    }
}