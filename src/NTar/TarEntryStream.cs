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

    /// <summary>
    /// True if the entry is a directory.
    /// </summary>
    public bool IsDirectory { get; internal set; }

    /// <summary>
    /// Unix mode/permissions of the entry (as an integer, octal-coded in header).
    /// </summary>
    public int Mode { get; internal set; } = 511; // 0777 dec

    /// <summary>
    /// User id of the entry owner.
    /// </summary>
    public int UserId { get; internal set; }

    /// <summary>
    /// Group id of the entry owner.
    /// </summary>
    public int GroupId { get; internal set; }

    /// <summary>
    /// User name of the entry owner (ustar uname field).
    /// </summary>
    public string UserName { get; internal set; }

    /// <summary>
    /// Group name of the entry owner (ustar gname field).
    /// </summary>
    public string GroupName { get; internal set; }

    /// <summary>
    /// Link name (for symlinks/hardlinks).
    /// </summary>
    public string LinkName { get; internal set; }

    /// <summary>
    /// Magic field (ustar).
    /// </summary>
    public string Magic { get; internal set; }

    /// <summary>
    /// UStar version field.
    /// </summary>
    public string Version { get; internal set; }

    /// <summary>
    /// Device major number (for character/block devices).
    /// </summary>
    public int DevMajor { get; internal set; }

    /// <summary>
    /// Device minor number (for character/block devices).
    /// </summary>
    public int DevMinor { get; internal set; }

    /// <summary>
    /// Prefix for long file names (ustar prefix field).
    /// </summary>
    public string Prefix { get; internal set; }

    /// <summary>
    /// Raw typeflag from the header as a char.
    /// </summary>
    public char TypeFlag { get; internal set; }

    /// <summary>
    /// Enum type from the typeflag.
    /// </summary>
    public TarEntryType Type
    {
        get
        {
            // NUL (0) is treated as a normal file entry in many tar implementations
            if (TypeFlag == '\0') return TarEntryType.File;
            return (TypeFlag >= '0' && TypeFlag <= '7')
                ? (TarEntryType)(byte)TypeFlag
                : TarEntryType.Unknown;
        }
        set
        {
            // Map enum back to the header typeflag byte. Unknown maps to NUL.
            TypeFlag = value == TarEntryType.Unknown ? '\0' : (char)(byte)value;
        }
    }

    public override void Flush()
    {
        // Forward to underlying stream when possible
        if (stream.CanWrite) stream.Flush();
    }

    public override long Seek(long offset, SeekOrigin origin)
    {
        if (!stream.CanSeek) throw new NotSupportedException();

        long target;
        switch (origin)
        {
            case SeekOrigin.Begin:
                target = start + offset;
                break;
            case SeekOrigin.Current:
                target = position + offset;
                break;
            case SeekOrigin.End:
                target = start + Length + offset;
                break;
            default:
                throw new ArgumentOutOfRangeException(nameof(origin));
        }

        if (target < start || target > start + Length) throw new ArgumentOutOfRangeException(nameof(offset));

        stream.Position = target;
        position = target;
        return position - start;
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

        int maxCount = (int)Math.Min(count, start + Length - position);
        if (maxCount <= 0) return 0;

        // Ensure underlying stream is positioned at our expected absolute position
        if (stream.CanSeek && stream.Position != position)
        {
            stream.Position = position;
        }

        int readCount = stream.Read(buffer, offset, maxCount);
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
        set
        {
            // allow repositioning within the entry when underlying stream supports seeking
            if (!stream.CanSeek) throw new NotSupportedException();
            if (value < 0 || value > Length) throw new ArgumentOutOfRangeException(nameof(value));
            Seek(value, SeekOrigin.Begin);
        }
    }
}
