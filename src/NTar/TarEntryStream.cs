using System;
using System.IO;

namespace NTar;

/// <summary>
/// An Tar entry stream for a file entry from a tar stream.
/// </summary>
/// <seealso cref="Stream" />
/// <remarks>
/// Initializes a new instance of the <see cref="TarEntryStream"/> class.
/// </remarks>
/// <param name="stream">The stream.</param>
/// <param name="start">The start.</param>
/// <param name="length">The length.</param>
/// <exception cref="ArgumentNullException"></exception>
public class TarEntryStream(Stream stream, long start, long length) : Stream
{
    private readonly Stream stream = stream ?? throw new ArgumentNullException(nameof(stream));
    private readonly long start = start;
    private long position = start;

    /// <summary>
    /// Gets the name of the file entry.
    /// </summary>
    public string FileName { get; internal set; }

    /// <summary>
    /// Gets the timestamp of the file entry.
    /// </summary>
    public DateTime LastModifiedTime { get; internal set; }

    public override void Flush()
    {
        throw new NotSupportedException();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        throw new NotSupportedException();
    }

    public override void SetLength(long value)
    {
        throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count)
    {
        if (buffer == null) throw new ArgumentNullException(nameof(buffer));
        if (count < 0) throw new ArgumentOutOfRangeException(nameof(count));
        if (offset < 0 || offset >= buffer.Length) throw new ArgumentOutOfRangeException(nameof(offset));
        if (count == 0) return 0;

        var maxCount = (int)Math.Min(count, start + Length - position);
        var readCount = stream.Read(buffer, offset, maxCount);
        position += readCount;
        return readCount;
    }

    public override void Write(byte[] buffer, int offset, int count)
    {
        throw new NotSupportedException();
    }

    public override bool CanRead => true;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override long Length { get; } = length;

    public override long Position
    {
        get => position - start;
        set => throw new NotSupportedException();
    }
}
