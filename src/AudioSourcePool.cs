// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using OpenTK.Audio.OpenAL;

namespace Playmaker.Audio;

internal sealed class AudioSourcePool : IDisposable
{
    private readonly AudioEngine _engine;
    private readonly Stack<int> _sources = new();
    private readonly HashSet<int> _allSources = new();
    private bool _disposed;

    internal AudioSourcePool(AudioEngine engine, int capacity)
    {
        _engine = engine;
        FillPool(capacity);
    }

    internal bool TryRent(out int source)
    {
        if (_sources.TryPop(out source))
        {
            return true;
        }
        source = 0;
        return false;
    }

    internal void Return(int source)
    {
        if (source == 0) return;
        _sources.Push(source);
    }

    internal int Count => _sources.Count;

    private void FillPool(int capacity)
    {
        int[] sources = new int[capacity];
        AL.GenSources(sources);
        Utils.CheckALError();
        _sources.Clear();
        _allSources.Clear();
        foreach (var source in sources)
        {
            _sources.Push(source);
            _allSources.Add(source);
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        try
        {
            if (_allSources.Count > 0)
            {
                var arr = _allSources.ToArray();
                AL.DeleteSources(arr);
                Utils.CheckALError();
            }
        }
        catch { }
        _sources.Clear();
        _allSources.Clear();
    }
}
