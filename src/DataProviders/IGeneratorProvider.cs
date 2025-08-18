// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

using Playmaker.Audio.Generators;

namespace Playmaker.Audio.Providers;

/// <summary>
/// The contract for a provider that can resolve a URI into a fully initialized Audio Generator.
/// </summary>
public interface IGeneratorProvider : IDisposable
{
    /// <summary>
    /// The URI schemes this provider is responsible for (e.g., "file", "http").
    /// </summary>
    string[] SupportedSchemes { get; }

    /// <summary>
    /// Asynchronously resolves a URI, creates the appropriate audio generator,
    /// and performs its initial loading/decoding.
    /// </summary>
    /// <param name="uri">The resource identifier.</param>
    /// <returns>A fully initialized AudioGeneratorBase on success, or null if the resource cannot be found or loaded.</returns>
    Task<AudioGeneratorBase?> CreateGeneratorAsync(Uri uri);
}