// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using System.Collections.Concurrent;
using Playmaker.Audio.Generators;

namespace Playmaker.Audio.Providers;

/// <summary>
/// A resource provider for local files that supports search paths, static asset caching, and streaming.
/// </summary>
public sealed class FileProvider : IGeneratorProvider
{
    private readonly AudioEngine _engine;
    private readonly List<string> _searchPaths = new();

    private readonly ConcurrentDictionary<string, Task<StaticSoundGenerator?>> _cache = new();

    /// <summary>
    /// When true (default), treats absolute URI paths (like /sfx/explosion.ogg) as relative to the search paths.
    /// When false, absolute paths are resolved from the file system root.
    /// </summary>
    public bool TreatAbsolutePathsAsRelative { get; set; } = true;

    public string[] SupportedSchemes => new[] { "file", "stream" };

    public FileProvider(AudioEngine engine)
    {
        _engine = engine;
    }

    public void AddSearchPath(string searchPath) => _searchPaths.Add(Path.GetFullPath(searchPath));
    public void ClearSearchPaths() => _searchPaths.Clear();

    public async Task<AudioGeneratorBase?> CreateGeneratorAsync(Uri uri)
    {
        bool isStreaming = uri.Scheme.Equals("stream", StringComparison.OrdinalIgnoreCase);

        if (!ResolvePath(uri, out var fullPath))
        {
            return null;
        }

        if (!isStreaming)
        {
            var task = _cache.GetOrAdd(fullPath!, path => CreateAndRegisterAsync(path));
            return await task.ConfigureAwait(false);
        }
        else
        {
            return await CreateStreamingGeneratorInternalAsync(fullPath!).ConfigureAwait(false);
        }
    }

    private async Task<StaticSoundGenerator?> CreateStaticGeneratorInternalAsync(string path)
    {
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var decoder = _engine.DecoderRegistry.CreateDecoder(path, stream);
            if (decoder is null) return null;
            var generator = new StaticSoundGenerator(_engine, decoder);
            await generator.InitializeAsync().ConfigureAwait(false);
            return generator;
        }
        catch { return null; }
    }

    private async Task<StaticSoundGenerator?> CreateAndRegisterAsync(string path)
    {
        var generator = await CreateStaticGeneratorInternalAsync(path).ConfigureAwait(false);
        if (generator is not null)
        {
            generator.Disposed += OnGeneratorDisposed;
        }
        return generator;
    }

    private async Task<StreamingSoundGenerator?> CreateStreamingGeneratorInternalAsync(string path)
    {
        try
        {
            var stream = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true);
            var decoder = _engine.DecoderRegistry.CreateDecoder(path, stream);
            if (decoder is null) return null;
            var generator = new StreamingSoundGenerator(_engine, decoder);
            await generator.InitializeAsync().ConfigureAwait(false);
            generator.SilentRelease();
            return generator;
        }
        catch { return null; }
    }

    private bool ResolvePath(Uri uri, out string? outPath)
    {
        string pathPart = uri.AbsolutePath.TrimStart('/');
        pathPart = pathPart.Replace('/', Path.DirectorySeparatorChar);

        if (!TreatAbsolutePathsAsRelative && Path.IsPathRooted(uri.AbsolutePath))
        {
            if (File.Exists(uri.AbsolutePath))
            {
                outPath = uri.AbsolutePath;
                return true;
            }
        }
        else
        {
            foreach (var basePath in _searchPaths)
            {
                var potentialPath = Path.Combine(basePath, pathPart);
                if (File.Exists(potentialPath))
                {
                    outPath = potentialPath;
                    return true;
                }
            }
        }

        outPath = null;
        return false;
    }

    private void OnGeneratorDisposed(AudioGeneratorBase generator)
    {
        if (generator is StaticSoundGenerator staticGen)
        {
            var item = _cache.FirstOrDefault(kvp => kvp.Value.IsCompletedSuccessfully && kvp.Value.Result == staticGen);
            if (!item.Equals(default(KeyValuePair<string, Task<StaticSoundGenerator?>>)) && item.Key is not null)
            {
                _cache.TryRemove(item.Key, out _);
            }
            staticGen.Disposed -= OnGeneratorDisposed;
        }
    }

    public void Dispose()
    {
        foreach (var task in _cache.Values)
        {
            if (task.IsCompletedSuccessfully && task.Result is { } generator)
            {
                generator.Disposed -= OnGeneratorDisposed;
                generator.DecrementReferenceCount();
            }
        }
        _cache.Clear();
    }
}