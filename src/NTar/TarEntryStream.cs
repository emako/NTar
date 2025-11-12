using System;
using System.Diagnostics;
using System.IO;

namespace NTar;

/// <summary>
/// Represents a view (slice) of an underlying stream that corresponds to a single
/// TAR entry's data region. This class wraps the original input stream and
/// exposes only the bytes belonging to the entry (from <c>start</c> for
/// <c>length</c> bytes). Consumers can read from this stream to obtain the
/// entry payload without affecting other entries.
/// </summary>
/// <remarks>
/// The instance does not copy the underlying data; it reads directly from the
/// provided <paramref name="stream"/>. When the underlying stream supports
/// seeking, this wrapper will also support seeking and will position the
/// underlying stream as needed. Writing and resizing are not supported.
/// </remarks>
/// <param name="stream">The underlying stream containing the tar archive.</param>
/// <param name="start">Absolute start position (within <paramref name="stream"/>) of the entry data.</param>
/// <param name="length">Number of bytes for this entry's data payload.</param>
/// <exception cref="ArgumentNullException">If <paramref name="stream"/> is null.</exception>
[DebuggerDisplay("{ToString()}")]
public partial class TarEntryStream(Stream stream, long start, long length) : Stream
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
    public bool IsDirectory
    {
        get => Type == TarEntryType.Directory;
        set
        {
            if (value) Type = TarEntryType.Directory;
            else throw new NotSupportedException("Use {Type} to instead.");
        }
    }

    /// <summary>
    /// Unix mode/permissions of the entry (as an integer, octal-coded in header).
    /// </summary>
    public int ModeFlag { get; internal set; } = (int)TarEntryMode.Full;

    /// <summary>
    /// Unix mode/permissions of the entry (as an enum in header).
    /// </summary>
    public TarEntryMode Mode
    {
        get => (TarEntryMode)ModeFlag;
        set => ModeFlag = (int)value;
    }

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

    /// <summary>
    /// Returns true if the underlying stream supports seeking. Seeking is only
    /// possible when the wrapped stream is seekable.
    /// </summary>
    public override bool CanSeek => stream.CanSeek;

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

    /// <summary>
    /// Helper string shown by the debugger (includes name, type, length and position).
    /// </summary>
    public override string ToString()
        => $"{(string.IsNullOrEmpty(FileName) ? "(no name)" : FileName)} {(IsDirectory ? "[dir]" : "[file]")} len={Length} pos={Position}";
}

public partial class TarEntryStream
{
    /// <summary>
    /// Create a read-only <see cref="TarEntryStream"/> backed by an in-memory buffer.
    /// </summary>
    /// <param name="bytes">The byte array containing the entry payload. If <c>null</c>, an empty entry is created.</param>
    /// <param name="fileName">Optional file name for the entry.</param>
    /// <param name="type">The <see cref="TarEntryType"/> for the entry (file, directory, symlink, etc.).</param>
    /// <param name="lastModifiedTime">Timestamp for the entry. If not provided, the default <see cref="DateTime"/> value is used.</param>
    /// <param name="mode">Unix permission bits for the entry. Defaults to <see cref="TarEntryMode.Full"/>.</param>
    /// <param name="userId">Numeric user id of the entry owner.</param>
    /// <param name="groupId">Numeric group id of the entry owner.</param>
    /// <param name="userName">Optional user name (ustar uname field).</param>
    /// <param name="groupName">Optional group name (ustar gname field).</param>
    /// <param name="callback">Optional callback invoked with the created <see cref="TarEntryStream"/> to allow further customization.</param>
    /// <returns>A new <see cref="TarEntryStream"/> that exposes the provided bytes as a read-only entry payload.</returns>
    /// <remarks>
    /// The produced <see cref="TarEntryStream"/> wraps a non-writable <see cref="MemoryStream"/> and does not copy data beyond the supplied byte array.
    /// The returned stream's <see cref="Length"/> equals the length of <paramref name="bytes"/> (or zero when <c>null</c> was provided).
    /// </remarks>
    public static TarEntryStream Create(
        byte[] bytes,
        string fileName = null,
        TarEntryType type = TarEntryType.File,
        DateTime lastModifiedTime = default,
        TarEntryMode mode = TarEntryMode.Full,
        int userId = 0,
        int groupId = 0,
        string userName = null,
        string groupName = null,
        Action<TarEntryStream> callback = null)
    {
        MemoryStream memoryStream = new(bytes ?? [], writable: false);
        TarEntryStream tarEntryStream = new(memoryStream, 0, bytes.LongLength)
        {
            FileName = fileName,
            LastModifiedTime = lastModifiedTime,
            Mode = mode,
            Type = type,
            UserId = userId,
            GroupId = groupId,
            UserName = userName,
            GroupName = groupName,
        };

        callback?.Invoke(tarEntryStream);
        return tarEntryStream;
    }
}
