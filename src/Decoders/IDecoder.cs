// Copyright 2025 the-byte-bender.
// SPDX-License-Identifier: MPL-2.0

namespace Playmaker.Audio.Decoders;

/// <summary>
/// The contract for a decoder that can transform an encoded audio stream into raw audio samples.
/// </summary>
public interface IDecoder : IDisposable
{
    /// <summary>
    /// Gets a value indicating whether the underlying stream supports seeking.
    /// </summary>
    bool CanSeek { get; }

    /// <summary>
    /// Gets the format of the decoded audio.
    /// This should be available after the decoder is initialized.
    /// </summary>
    AudioFormat Format { get; }

    /// <summary>
    /// Gets the total duration of the audio stream, if known.
    /// Returns null for infinite or non-seekable streams where duration cannot be determined.
    /// </summary>
    TimeSpan? Duration { get; }

    /// <summary>
    /// Reads from the underlying stream and decodes the next chunk of audio.
    /// This is a synchronous, potentially blocking call. The audio data is interleaved.
    /// </summary>
    /// <param name="pcmBuffer">
    /// A span of memory to fill with decoded 16-bit PCM samples.
    /// The length of this buffer must be a multiple of the number of channels.
    /// For example, to read 1024 sample frames of stereo audio, the buffer must have a length of at least 2048.
    /// </param>
    /// <returns>
    /// The number of **sample frames** (not total samples or bytes) that were successfully decoded.
    /// Returns 0 when the end of the stream is reached.
    /// </returns>    
    int Decode(Span<short> pcmBuffer);

    /// <summary>
    /// Attempts to seek to a new position in the audio stream.
    /// </summary>
    /// <param name="position">The time position to seek to.</param>
    /// <returns>True if the seek was successful, otherwise false.</returns>
    bool TrySeek(TimeSpan position);

    /// <summary>
    /// Resets the decoder to the beginning of the audio stream.
    /// Functionally equivalent to seeking to TimeSpan.Zero.
    /// </summary>
    /// <returns>True if the rewind was successful.</returns>
    bool TryRewind();
}