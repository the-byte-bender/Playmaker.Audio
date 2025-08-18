// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

namespace Playmaker.Audio.Decoders;

public delegate IDecoder DecoderActivator(Stream stream);

/// <summary>
/// A centralized factory for creating the correct IDecoder for a given resource URI.
/// It manages registrations for different file extensions.
/// </summary>
public class DecoderRegistry
{
    private readonly Dictionary<string, DecoderActivator> _activators = new(StringComparer.OrdinalIgnoreCase);
    private DecoderActivator? _fallbackActivator;

    /// <summary>
    /// Registers a decoder activator for a specific file extension.
    /// </summary>
    /// <param name="extension">The file extension, including the dot (e.g., ".ogg").</param>
    /// <param name="activator">A function that creates an instance of the decoder.</param>
    public void Register(string extension, DecoderActivator activator)
    {
        _activators[extension] = activator;
    }

    /// <summary>
    /// Registers a fallback decoder to try when no specific extension is matched.
    /// </summary>
    public void RegisterFallback(DecoderActivator activator)
    {
        _fallbackActivator = activator;
    }

    /// <summary>
    /// Creates an appropriate decoder for the given URI.
    /// </summary>
    /// <param name="uri">The URI of the resource to be decoded.</param>
    /// <param name="stream">The data stream for the resource.</param>
    /// <returns>An initialized IDecoder, or null if no suitable decoder was found.</returns>
    public IDecoder? CreateDecoder(string path, Stream stream)
    {
        var extension = Path.GetExtension(path);

        if (!string.IsNullOrEmpty(extension))
        {
            if (_activators.TryGetValue(extension, out var activator))
            {
                return activator(stream);
            }
        }

        if (_fallbackActivator is not null)
        {
            return _fallbackActivator(stream);
        }

        return null;
    }
}