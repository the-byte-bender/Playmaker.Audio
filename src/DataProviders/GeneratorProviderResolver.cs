// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using Playmaker.Audio.Generators;

namespace Playmaker.Audio.Providers;

public sealed class GeneratorProviderResolver : IDisposable
{
    private readonly Dictionary<string, IGeneratorProvider> _providersByScheme = new(StringComparer.OrdinalIgnoreCase);
    private readonly HashSet<IGeneratorProvider> _allProviders = new();
    private readonly object _lock = new();

    /// <summary>
    /// Scheme used when an input string has no scheme (a relative path or bare filename).
    /// Must correspond to a registered provider.
    /// </summary>
    public string? DefaultScheme { get; set; }

    /// <summary>
    /// Registers a provider for all of its supported schemes. 
    /// </summary>
    public void Register(IGeneratorProvider provider)
    {
        lock (_lock)
        {
            foreach (var scheme in provider.SupportedSchemes)
            {
                if (string.IsNullOrWhiteSpace(scheme)) continue;
                _providersByScheme[scheme] = provider;
            }
            _allProviders.Add(provider);
        }
    }

    /// <summary>
    /// Unregisters a provider. Any schemes it provided will be removed.
    /// </summary>
    public void Unregister(IGeneratorProvider provider)
    {
        lock (_lock)
        {
            var removedSchemes = _providersByScheme.Where(k => k.Value == provider).Select(k => k.Key).ToList();
            foreach (var scheme in removedSchemes)
                _providersByScheme.Remove(scheme);
            _allProviders.Remove(provider);
            if (DefaultScheme is not null && removedSchemes.Contains(DefaultScheme, StringComparer.OrdinalIgnoreCase))
                DefaultScheme = null;
        }
    }

    /// <summary>
    /// Attempts to create a generator from a URI or relative path.
    /// Relative / bare paths are routed to the provider for <see cref="DefaultScheme"/>.
    /// </summary>
    public Task<AudioGeneratorBase?> CreateGeneratorAsync(string uriOrPath)
    {
        if (TryParseAbsoluteUri(uriOrPath, out var uri) && uri is not null)
        {
            if (!_providersByScheme.TryGetValue(uri.Scheme, out var provider))
                return Task.FromResult<AudioGeneratorBase?>(null);
            return provider.CreateGeneratorAsync(uri);
        }
        var scheme = DefaultScheme;
        if (scheme is null) return Task.FromResult<AudioGeneratorBase?>(null);
        if (!_providersByScheme.TryGetValue(scheme, out var defaultProvider))
            return Task.FromResult<AudioGeneratorBase?>(null);
        var sanitized = uriOrPath.Replace('\\', '/').TrimStart('/');
        var generated = new Uri($"{scheme}:///{sanitized}");
        return defaultProvider.CreateGeneratorAsync(generated);
    }

    /// <summary>
    /// Direct creation when a fully built Uri is already available.
    /// </summary>
    public Task<AudioGeneratorBase?> CreateGeneratorAsync(Uri uri)
    {
        if (!_providersByScheme.TryGetValue(uri.Scheme, out var provider))
            return Task.FromResult<AudioGeneratorBase?>(null);
        return provider.CreateGeneratorAsync(uri);
    }

    private static bool TryParseAbsoluteUri(string text, out Uri? uri)
    {
        int colon = text.IndexOf(':');
        if (colon > 0)
        {
            for (int i = 0; i < colon; i++)
            {
                char c = text[i];
                if (!char.IsLetterOrDigit(c) && c != '+' && c != '-' && c != '.')
                {
                    uri = null!; return false;
                }
            }
            if (Uri.TryCreate(text, UriKind.Absolute, out var created) && created is not null)
            {
                uri = created;
                return true;
            }
        }
        uri = null; return false;
    }

    public void Dispose()
    {
        lock (_lock)
        {
            foreach (var p in _allProviders.ToArray())
            {
                try { p.Dispose(); } catch { }
            }
            _allProviders.Clear();
            _providersByScheme.Clear();
            DefaultScheme = null;
        }
    }
}
