// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using System.Numerics;
using OpenTK.Audio.OpenAL;

namespace Playmaker.Audio;

public sealed class AudioListener
{
    private readonly AudioEngine _engine;
    private Vector3 _position = Vector3.Zero;
    private Vector3 _velocity = Vector3.Zero;
    private Vector3 _forward = new(0, 0, -1);
    private Vector3 _up = Vector3.UnitY;

    private bool _dirtyTransform = true;
    private bool _dirtyOrientation = true;

    internal AudioListener(AudioEngine engine)
    {
        _engine = engine;
    }

    public Vector3 Position => _position;
    public Vector3 Velocity => _velocity;
    public Vector3 Forward => _forward;
    public Vector3 Up => _up;

    /// <summary>
    /// Sets listener world transform (position + velocity).
    /// </summary>
    public void SetTransform(Vector3 position, Vector3 velocity)
        => _engine.AudioThreadMarshaller.Invoke(() =>
        {
            if (_position == position && _velocity == velocity) return;
            _position = position;
            _velocity = velocity;
            _dirtyTransform = true;
        });

    /// <summary>
    /// Sets listener orientation vectors. They will be normalized if not already.
    /// </summary>
    public void SetOrientation(Vector3 forward, Vector3 up)
        => _engine.AudioThreadMarshaller.Invoke(() =>
        {
            if (forward == _forward && up == _up) return;
            if (forward.LengthSquared() > 0) forward = Vector3.Normalize(forward);
            if (up.LengthSquared() > 0) up = Vector3.Normalize(up);
            _forward = forward;
            _up = up;
            _dirtyOrientation = true;
        });

    internal void ApplyPending()
    {
        if (_dirtyTransform)
        {
            AL.Listener(ALListener3f.Position, _position.X, _position.Y, _position.Z);
            AL.Listener(ALListener3f.Velocity, _velocity.X, _velocity.Y, _velocity.Z);
            Utils.CheckALError(false);
            _dirtyTransform = false;
        }
        if (_dirtyOrientation)
        {
            Span<float> ori = stackalloc float[6] { _forward.X, _forward.Y, _forward.Z, _up.X, _up.Y, _up.Z };
            AL.Listener(ALListenerfv.Orientation, ref ori[0]);
            Utils.CheckALError(false);
            _dirtyOrientation = false;
        }
    }
}
