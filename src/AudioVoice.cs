// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using OpenTK.Audio.OpenAL;
using Playmaker.Audio.Generators;

namespace Playmaker.Audio;

[Flags]
internal enum DirtyFlags
{
    None = 0,
    Gain = 1 << 0,
    Pitch = 1 << 1,
    Transform = 1 << 2,
    Looping = 1 << 3,
    MixState = 1 << 4,
    EffectivePriority = 1 << 5,
    Attenuation = 1 << 6,
    All = ~0
}

public enum MixState
{
    Direct,
    Relative,
    Spatialized
}

public enum VoiceState
{
    Stopped,
    PlayingPhysical,
    PausedPhysical,
    PlayingVirtual,
    PausedVirtual,
    Disposed
}

public sealed class AudioVoice : IDisposable
{
    private readonly AudioEngine _engine;
    private AudioGeneratorBase _generator;
    public bool IsOneShot { get; }

    private int _lastKnownBusVersion = -1;
    private int _lastKnownEmitterVersion = -1;
    private DirtyFlags _dirtyFlags = DirtyFlags.All;
    private float _logicalPlaybackTime = 0.0f;
    private float _lastAppliedGain;
    private float _lastAppliedPitch;
    private Vector3 _lastAppliedPosition;
    private Vector3 _lastAppliedVelocity;
    private bool _lastAppliedLooping;
    private MixState _lastAppliedMixState;

    internal int RawSource { get; private set; } = 0;

    private float _gain = 1.0f;
    public float Gain => _gain;
    private float _pitch = 1.0f;
    public float Pitch => _pitch;
    private bool _looping = false;
    public bool Looping => _looping;
    private bool _relative = false;
    private MixState _mixState = MixState.Spatialized;
    public MixState MixState => _mixState;
    private Vector3 _position = Vector3.Zero;
    public Vector3 Position => _position;
    private Vector3 _velocity = Vector3.Zero;
    public Vector3 Velocity => _velocity;
    private AudioEmitter? _emitter = null;
    public AudioEmitter? Emitter => _emitter;
    private int _priority = 1;
    public int Priority => _priority;
    private int _effectivePriority = 1;
    public int EffectivePriority => _effectivePriority;
    private float _rolloffFactor = 1.0f;
    public float RolloffFactor => _rolloffFactor;
    private float _referenceDistance = 1.0f;
    public float ReferenceDistance => _referenceDistance;
    private float _maxDistance = float.MaxValue;
    public float MaxDistance => _maxDistance;

    public VoiceState State { get; internal set; } = VoiceState.Stopped;
    private AudioBus _bus;
    public AudioBus Bus => _bus;

    internal AudioVoice(AudioEngine engine, AudioGeneratorBase generator, AudioBus initialBus, bool isOneShot)
    {
        _engine = engine;
        _generator = generator;
        _bus = initialBus;
        IsOneShot = isOneShot;
        _engine.AudioThreadMarshaller.Invoke(() =>
        {
            _generator.AttachTo(this);
            _engine.RegisterVoice(this);
        });
    }

    public void SetGain(float gain) => _engine.AudioThreadMarshaller.Invoke(() => { _gain = gain; _dirtyFlags |= DirtyFlags.Gain; });

    public void SetPitch(float pitch) => _engine.AudioThreadMarshaller.Invoke(() => { _pitch = pitch; _dirtyFlags |= DirtyFlags.Pitch; });

    public void SetLooping(bool looping) => _engine.AudioThreadMarshaller.Invoke(() => { _looping = looping; _dirtyFlags |= DirtyFlags.Looping; });

    public void SetMixState(MixState state) => _engine.AudioThreadMarshaller.Invoke(() =>
    {
        if (_mixState == state) return;
        _mixState = state;
        switch (state)
        {
            case MixState.Direct:
                _relative = false;
                break;
            case MixState.Relative:
                _relative = true;
                break;
            case MixState.Spatialized:
                _relative = false;
                break;
        }
        _dirtyFlags |= DirtyFlags.MixState;
    });

    public void SetTransform(Vector3 position, Vector3 velocity) => _engine.AudioThreadMarshaller.Invoke(() => { _position = position; _velocity = velocity; _dirtyFlags |= DirtyFlags.Transform; });

    public void SetPosition(Vector3 position) => _engine.AudioThreadMarshaller.Invoke(() => { _position = position; _dirtyFlags |= DirtyFlags.Transform; });

    public void SetVelocity(Vector3 velocity) => _engine.AudioThreadMarshaller.Invoke(() => { _velocity = velocity; _dirtyFlags |= DirtyFlags.Transform; });

    public void SetPriority(int priority) => _engine.AudioThreadMarshaller.Invoke(() => { _priority = priority; _dirtyFlags |= DirtyFlags.EffectivePriority; });

    public void SetRolloff(float rolloff) => _engine.AudioThreadMarshaller.Invoke(() => { _rolloffFactor = rolloff; _dirtyFlags |= DirtyFlags.Attenuation; });
    public void SetReferenceDistance(float distance) => _engine.AudioThreadMarshaller.Invoke(() => { _referenceDistance = distance; _dirtyFlags |= DirtyFlags.Attenuation; });
    public void SetMaxDistance(float distance) => _engine.AudioThreadMarshaller.Invoke(() => { _maxDistance = distance; _dirtyFlags |= DirtyFlags.Attenuation; });

    public void AttachTo(AudioEmitter? newEmitter) => _engine.AudioThreadMarshaller.Invoke(() =>
    {
        AttachToInternal(newEmitter);
    });

    internal void AttachToInternal(AudioEmitter? newEmitter)
    {
        var oldEmitter = _emitter;
        if (oldEmitter == newEmitter) return;
        _emitter = newEmitter;
        _lastKnownEmitterVersion = newEmitter?.Version ?? -1;
        _dirtyFlags |= DirtyFlags.Transform | DirtyFlags.EffectivePriority;
    }

    internal void Update(float deltaTime)
    {
        if (State == VoiceState.Stopped || State == VoiceState.Disposed) return;
        if (State == VoiceState.PlayingVirtual)
        {
            UpdateVirtualState(deltaTime);
            return;
        }

        if (State != VoiceState.PlayingPhysical && State != VoiceState.PausedPhysical) return;

        if (_generator is StreamingAudioGeneratorBase streaming && RawSource != 0)
        {
            PumpStreaming(streaming);
        }
        else if (_generator is StaticAudioGeneratorBase && State == VoiceState.PlayingPhysical)
        {
            var alState = (ALSourceState)AL.GetSource(RawSource, ALGetSourcei.SourceState);
            if (alState == ALSourceState.Stopped && !Looping)
            {
                StopImmediate();
                return;
            }
        }
        CheckDependencies();
        if (_dirtyFlags == DirtyFlags.None) return;
        ProcessDirtyFlags();
        _dirtyFlags = DirtyFlags.None;
    }

    private void PumpStreaming(StreamingAudioGeneratorBase streaming)
    {
        int processed = AL.GetSource(RawSource, ALGetSourcei.BuffersProcessed);
        if (processed > 0)
        {
            while (processed-- > 0)
            {
                int buf = AL.SourceUnqueueBuffer(RawSource);
                Utils.CheckALError(false);
                streaming.ReturnFreeBuffer(buf);
            }
        }

        while (streaming.TryGetFilledBuffer(out int filled))
        {
            AL.SourceQueueBuffer(RawSource, filled);
            Utils.CheckALError(false);
        }

        var state = (ALSourceState)AL.GetSource(RawSource, ALGetSourcei.SourceState);
        if (state != ALSourceState.Playing && State == VoiceState.PlayingPhysical)
        {
            int queued = AL.GetSource(RawSource, ALGetSourcei.BuffersQueued);
            if (queued > 0)
            {
                AL.SourcePlay(RawSource);
                Utils.CheckALError();
            }
            else
            {
                if (streaming.EndOfStream)
                {
                    if (!Looping)
                    {
                        StopImmediate();
                    }
                    else
                    {
                        _logicalPlaybackTime = 0f;
                        if (streaming.CanSeek)
                        {
                            streaming.Seek(TimeSpan.Zero);
                        }
                    }
                }
            }
        }
    }

    private void UpdateVirtualState(float deltaTime)
    {
        float effectivePitch = Pitch * Bus.EffectivePitch;
        _logicalPlaybackTime += deltaTime * effectivePitch;
        if (!Looping && _generator.Duration.HasValue && _logicalPlaybackTime >= _generator.Duration.Value.TotalSeconds)
        {
            var oldState = State;
            State = VoiceState.Stopped;
            _engine.OnVoiceStateChanged(this, oldState);
        }
        else if (Looping && _generator.Duration.HasValue && _logicalPlaybackTime >= _generator.Duration.Value.TotalSeconds)
        {
            _logicalPlaybackTime = 0f;
        }
    }

    private void CheckDependencies()
    {
        if (Bus.Version != _lastKnownBusVersion)
        {
            _lastKnownBusVersion = Bus.Version;
            _dirtyFlags |= DirtyFlags.Gain | DirtyFlags.Pitch | DirtyFlags.EffectivePriority;
        }

        if (_emitter is not null && _emitter.Version != _lastKnownEmitterVersion)
        {
            _lastKnownEmitterVersion = _emitter.Version;
            _dirtyFlags |= DirtyFlags.Transform | DirtyFlags.EffectivePriority;
        }
    }

    private void ProcessDirtyFlags(bool force = false)
    {
        if (_dirtyFlags.HasFlag(DirtyFlags.Gain))
        {
            float effectiveGain = Gain * Bus.EffectiveGain;
            if (force || Math.Abs(effectiveGain - _lastAppliedGain) > 0.001f)
            {
                AL.Source(RawSource, ALSourcef.Gain, effectiveGain);
                _lastAppliedGain = effectiveGain;
            }
        }

        if (_dirtyFlags.HasFlag(DirtyFlags.Pitch))
        {
            float effectivePitch = Pitch * Bus.EffectivePitch;
            if (force || Math.Abs(effectivePitch - _lastAppliedPitch) > 0.001f)
            {
                AL.Source(RawSource, ALSourcef.Pitch, effectivePitch);
                _lastAppliedPitch = effectivePitch;
            }
        }

        if (_dirtyFlags.HasFlag(DirtyFlags.Transform))
        {
            var (worldPos, worldVel) = CalculateWorldTransform();
            if (force || worldPos != _lastAppliedPosition)
            {
                AL.Source(RawSource, ALSource3f.Position, worldPos.X, worldPos.Y, worldPos.Z);
                _lastAppliedPosition = worldPos;
            }
            if (force || worldVel != _lastAppliedVelocity)
            {
                AL.Source(RawSource, ALSource3f.Velocity, worldVel.X, worldVel.Y, worldVel.Z);
                _lastAppliedVelocity = worldVel;
            }
        }
        if (_dirtyFlags.HasFlag(DirtyFlags.MixState))
        {
            if (force || _mixState != _lastAppliedMixState)
            {
                AL.Source(RawSource, ALSourceb.SourceRelative, _relative);
                switch (_mixState)
                {
                    case MixState.Direct:
                        Utils.SetSpatialize(RawSource, Spatialize.Disabled);
                        Utils.SetDirectChannels(RawSource, true);
                        break;
                    case MixState.Relative:
                    case MixState.Spatialized:
                        Utils.SetDirectChannels(RawSource, false);
                        Utils.SetSpatialize(RawSource, Spatialize.Enabled);
                        break;
                }
                _lastAppliedMixState = _mixState;
            }
        }

        if (_dirtyFlags.HasFlag(DirtyFlags.EffectivePriority))
        {
            _effectivePriority = _priority + (_emitter?.PriorityBias ?? 0) + Bus.EffectivePriorityBias;
        }

        if (_dirtyFlags.HasFlag(DirtyFlags.Looping))
        {
            if (force || Looping != _lastAppliedLooping)
            {
                AL.Source(RawSource, ALSourceb.Looping, Looping);
                _lastAppliedLooping = Looping;
            }
        }

        if (_dirtyFlags.HasFlag(DirtyFlags.Attenuation))
        {
            AL.Source(RawSource, ALSourcef.RolloffFactor, _rolloffFactor);
            AL.Source(RawSource, ALSourcef.ReferenceDistance, _referenceDistance);
            if (_maxDistance > 0f)
            {
                AL.Source(RawSource, ALSourcef.MaxDistance, _maxDistance);
            }
        }
    }

    internal void Hydrate(int rawSource)
    {
        if (!AL.IsSource(rawSource)) return;
        RawSource = rawSource;
        _lastKnownBusVersion = Bus.Version;
        _lastKnownEmitterVersion = Emitter?.Version ?? -1;

        _dirtyFlags = DirtyFlags.All;
        ProcessDirtyFlags(true);
        _dirtyFlags = DirtyFlags.None;

        ConnectGeneratorToSource();
        if (_logicalPlaybackTime > 0)
        {
            AL.Source(RawSource, ALSourcef.SecOffset, _logicalPlaybackTime);
            Utils.CheckALError(false);
        }
        if (State == VoiceState.PlayingPhysical)
        {
            AL.SourcePlay(RawSource);
        }
    }

    private void ConnectGeneratorToSource()
    {
        if (!AL.IsSource(RawSource)) return;

        if (_generator is StaticAudioGeneratorBase staticGen)
        {
            AL.Source(RawSource, ALSourcei.Buffer, staticGen.RawBuffer);
        }
        else if (_generator is StreamingAudioGeneratorBase streamingGen)
        {
            while (streamingGen.TryGetFilledBuffer(out int buffer))
            {
                AL.SourceQueueBuffer(RawSource, buffer);
            }
        }
    }

    private void DisconnectGeneratorFromSource()
    {
        if (!AL.IsSource(RawSource)) return;

        AL.SourceStop(RawSource);
        if (_generator is StaticAudioGeneratorBase)
        {
            AL.Source(RawSource, ALSourcei.Buffer, 0);
        }
        else if (_generator is StreamingAudioGeneratorBase streamGen)
        {
            var queuedBuffers = AL.GetSource(RawSource, ALGetSourcei.BuffersQueued);
            for (int i = 0; i < queuedBuffers; i++)
            {
                streamGen.ReturnFreeBuffer(AL.SourceUnqueueBuffer(RawSource));
            }
        }
        Utils.CheckALError(false);
    }

    internal void MakeVirtual()
    {
        if (!AL.IsSource(RawSource)) return;
        var oldState = State;
        _logicalPlaybackTime = AL.GetSource(RawSource, ALSourcef.SecOffset);
        Utils.CheckALError(false);
        DisconnectGeneratorFromSource();
        _engine.ReturnSource(this, RawSource);
        RawSource = 0;
        if (State == VoiceState.PlayingPhysical)
        {
            State = VoiceState.PlayingVirtual;
        }
        else if (State == VoiceState.PausedPhysical)
        {
            State = VoiceState.PausedVirtual;
        }
        _engine.OnVoiceStateChanged(this, oldState);
    }

    internal void MakePhysical(int rawSource)
    {
        var oldState = State;
        if (State == VoiceState.PlayingPhysical || State == VoiceState.PausedPhysical || State == VoiceState.Stopped) return;
        if (State == VoiceState.PlayingVirtual)
        {
            State = VoiceState.PlayingPhysical;
        }
        else if (State == VoiceState.PausedVirtual)
        {
            State = VoiceState.PausedPhysical;
        }
        else
        {
            return;
        }
        Hydrate(rawSource);
        _engine.OnVoiceStateChanged(this, oldState);
    }

    private (Vector3 Position, Vector3 Velocity) CalculateWorldTransform()
    {
        if (_emitter is not null)
        {
            var worldPos = _emitter.Position + _position;
            var worldVel = _emitter.Velocity + _velocity;
            return (worldPos, worldVel);
        }

        return (_position, _velocity);
    }

    public void Dispose()
    {
        _engine.AudioThreadMarshaller.Invoke(DisposeImediate);
    }

    internal void DisposeImediate()
    {
        if (State == VoiceState.Disposed) return;
        var oldState = State;
        State = VoiceState.Disposed;
        _emitter = null;

        if ((oldState == VoiceState.PlayingPhysical || oldState == VoiceState.PausedPhysical) && AL.IsSource(RawSource))
        {
            AL.SourceStop(RawSource);
            DisconnectGeneratorFromSource();
            _engine.ReturnSource(this, RawSource);
            RawSource = 0;
        }

        _generator.DetachFrom(this);
        _engine.UnregisterVoice(this);
    }

    public void Play() => _engine.AudioThreadMarshaller.Invoke(PlayImmediate);

    internal void PlayImmediate()
    {
        if (State == VoiceState.Disposed) return;
        if (State == VoiceState.PlayingPhysical || State == VoiceState.PlayingVirtual) return;
        var oldState = State;
        switch (State)
        {
            case VoiceState.Stopped:
                if (_engine.TryGetSource(this, out int src))
                {
                    State = VoiceState.PlayingPhysical;
                    Hydrate(src);
                }
                else
                {
                    State = VoiceState.PlayingVirtual;
                }
                break;
            case VoiceState.PausedPhysical:
                if (AL.IsSource(RawSource))
                {
                    AL.SourcePlay(RawSource);
                    Utils.CheckALError();
                    State = VoiceState.PlayingPhysical;
                }
                else
                {
                    State = VoiceState.PlayingVirtual;
                }
                break;
            case VoiceState.PausedVirtual:
                State = VoiceState.PlayingVirtual;
                break;
        }
        if (State != oldState)
        {
            _engine.OnVoiceStateChanged(this, oldState);
        }
    }

    public void Pause() => _engine.AudioThreadMarshaller.Invoke(PauseImmediate);

    internal void PauseImmediate()
    {
        if (State == VoiceState.Disposed) return;
        if (State == VoiceState.PausedPhysical || State == VoiceState.PausedVirtual || State == VoiceState.Stopped) return;
        var oldState = State;
        if (State == VoiceState.PlayingPhysical)
        {
            if (AL.IsSource(RawSource))
            {
                AL.SourcePause(RawSource);
                Utils.CheckALError();
                State = VoiceState.PausedPhysical;
            }
            else
            {
                State = VoiceState.PausedVirtual;
            }
        }
        else if (State == VoiceState.PlayingVirtual)
        {
            State = VoiceState.PausedVirtual;
        }
        _engine.OnVoiceStateChanged(this, oldState);
    }

    public void Rewind() => _engine.AudioThreadMarshaller.Invoke(RewindImmediate);

    internal void RewindImmediate()
    {
        if (State == VoiceState.Disposed || State == VoiceState.Stopped) return;
        _logicalPlaybackTime = 0f;
        if ((State == VoiceState.PlayingPhysical || State == VoiceState.PausedPhysical) && AL.IsSource(RawSource))
        {
            AL.SourceRewind(RawSource);
            Utils.CheckALError();
        }
        if (_generator is StreamingAudioGeneratorBase streaming && streaming.CanSeek)
        {
            streaming.Seek(TimeSpan.Zero);
        }
    }

    public void Stop() => _engine.AudioThreadMarshaller.Invoke(StopImmediate);

    internal void StopImmediate()
    {
        if (State == VoiceState.Disposed || State == VoiceState.Stopped) return;
        var oldState = State;
        if ((State == VoiceState.PlayingPhysical || State == VoiceState.PausedPhysical) && AL.IsSource(RawSource))
        {
            AL.SourceStop(RawSource);
            DisconnectGeneratorFromSource();
            Utils.CheckALError();
            _engine.ReturnSource(this, RawSource);
            RawSource = 0;
        }
        _logicalPlaybackTime = 0f;
        if (_generator is StreamingAudioGeneratorBase streaming && streaming.CanSeek)
        {
            streaming.Seek(TimeSpan.Zero);
        }
        State = VoiceState.Stopped;
        _engine.OnVoiceStateChanged(this, oldState);
    }
}