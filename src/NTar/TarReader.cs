using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace NTar;

/// <summary>
/// A tar reader to untar a stream.
/// </summary>
public static class TarReader
{
    internal static readonly DateTime Epoch = new(1970, 1, 1, 0, 0, 0, DateTimeKind.Utc);

    /// <summary>
    /// Untars a stream to a specified output directory.
    /// </summary>
    /// <param name="stream">The stream.</param>
    /// <param name="outputDirectory">The output directory.</param>
    /// <exception cref="System.ArgumentNullException"></exception>
    /// <exception cref="InvalidDataException">If an invalid entry was found</exception>
    public static void Extract(this Stream stream, string outputDirectory)
    {
        if (outputDirectory == null) throw new ArgumentNullException(nameof(outputDirectory));

        outputDirectory = Path.GetFullPath(outputDirectory);

        // Untar the stream
        foreach (var entryStream in stream.Read())
        {
            if (entryStream.IsDirectory)
            {
                // Directory entry: create the directory and set its last write time
                var dirPath = Path.Combine(outputDirectory, entryStream.FileName ?? string.Empty);
                // Trim any trailing separators that may be present in the tar entry name
                dirPath = dirPath.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
                Directory.CreateDirectory(dirPath);
                try
                {
                    Directory.SetLastWriteTime(dirPath, entryStream.LastModifiedTime);
                }
                catch
                {
                    // Ignore failures to set directory timestamp
                }
            }
            else
            {
                var outputFile = Path.Combine(outputDirectory, entryStream.FileName);
                var outputDir = Path.GetDirectoryName(outputFile);
                Directory.CreateDirectory(outputDir);
                using (var outputStream = new FileStream(outputFile, FileMode.Create, FileAccess.Write))
                {
                    entryStream.CopyTo(outputStream);
                }
                File.SetLastWriteTime(outputFile, entryStream.LastModifiedTime);
            }
        }
    }

    /// <summary>
    /// Untars the specified input stream and returns a stream for each file entry.
    /// </summary>
    /// <param name="inputStream">The input stream.</param>
    /// <returns>An enumeration of file entries. The inputstream can be read on each entry with a length of <see cref="TarEntryStream.Length"/></returns>
    /// <exception cref="InvalidDataException">If an invalid entry was found</exception>
    public static IEnumerable<TarEntryStream> Read(this Stream inputStream)
    {
        var header = new byte[512];

        long position = 0;

        if (inputStream.CanSeek)
            inputStream.Position = 0;

        while (true)
        {
            int zeroBlockCount = 0;
            while (true)
            {
                // Read the 512 byte block header
                int length = inputStream.Read(header, 0, header.Length);
                if (length < 512)
                {
                    throw new InvalidDataException($"Invalid header block size < 512");
                }
                position += length;

                // Check if the block is full of zero
                bool isZero = true;
                for (int i = 0; i < header.Length; i++)
                {
                    if (header[i] != 0)
                    {
                        isZero = false;
                        break;
                    }
                }

                if (isZero)
                {
                    // If it is full of zero two consecutive times, we have to exit
                    zeroBlockCount++;
                    if (zeroBlockCount == 1)
                    {
                        yield break;
                    }
                }
                else
                {
                    break;
                }
            }

            // Read file name
            var fileName = GetString(header, 0, 100);

            // Read mode, uid and gid
            var modeRead = ReadOctal(header, 100, 8);
            var uidRead = ReadOctal(header, 108, 8);
            var gidRead = ReadOctal(header, 116, 8);

            // Read linkname
            var linkName = GetString(header, 157, 100);

            // Read user and group names (ustar fields)
            var userName = GetString(header, 265, 32);
            var groupName = GetString(header, 297, 32);

            // Read magic/version and normalize (trim whitespace)
            var magicRaw = GetString(header, 257, 8);
            var versionRaw = GetString(header, 263, 2);
            var magic = string.IsNullOrWhiteSpace(magicRaw) ? null : magicRaw.Trim();
            var version = string.IsNullOrWhiteSpace(versionRaw) ? null : versionRaw.Trim();

            // Read device major/minor
            var devMajorRead = ReadOctal(header, 329, 8);
            var devMinorRead = ReadOctal(header, 337, 8);

            // Read prefix (for long file names)
            var prefix = GetString(header, 345, 155);

            // read checksum
            var checksum = ReadOctal(header, 148, 8);
            if (!checksum.HasValue)
            {
                throw new InvalidDataException($"Invalid checksum for file entry [{fileName}] ");
            }

            // verify checksum
            uint checksumVerif = 0;
            for (int i = 0; i < header.Length; i++)
            {
                var c = header[i];
                if (i >= 148 && i < (148 + 8))
                {
                    c = 32;
                }
                checksumVerif += c;
            }

            // Checksum is invalid, exit
            if (checksum.Value != checksumVerif)
            {
                throw new InvalidDataException($"Invalid checksum verification for file entry [{fileName}] ");
            }

            // Read file size
            var fileSizeRead = ReadOctal(header, 124, 12);
            if (!fileSizeRead.HasValue)
            {
                throw new InvalidDataException($"Invalid filesize for file entry [{fileName}] ");
            }

            var fileLength = fileSizeRead.Value;

            // Read the type of the file entry
            var type = header[156];
            // We support file and directory types
            TarEntryStream tarEntryStream = null;
            if (type == '0' || type == 0)
            {
                // Read timestamp
                var unixTimeStamp = ReadOctal(header, 136, 12);
                if (!unixTimeStamp.HasValue)
                {
                    throw new InvalidDataException($"Invalid timestamp for file entry [{fileName}] ");
                }
                var lastModifiedTime = Epoch.AddSeconds(unixTimeStamp.Value).ToLocalTime();

                // If ustar, load prefix filename
                if (magic == "ustar")
                {
                    fileName = prefix + fileName;
                }

                tarEntryStream = new TarEntryStream(inputStream, position, fileLength)
                {
                    FileName = fileName,
                    LastModifiedTime = lastModifiedTime,
                    IsDirectory = false,
                    Mode = modeRead.HasValue ? (int)modeRead.Value : 0,
                    UserId = uidRead.HasValue ? (int)uidRead.Value : 0,
                    GroupId = gidRead.HasValue ? (int)gidRead.Value : 0,
                    UserName = string.IsNullOrEmpty(userName) ? null : userName,
                    GroupName = string.IsNullOrEmpty(groupName) ? null : groupName,
                    LinkName = string.IsNullOrEmpty(linkName) ? null : linkName,
                    Magic = string.IsNullOrEmpty(magic) ? null : magic,
                    Version = string.IsNullOrEmpty(version) ? null : version,
                    DevMajor = devMajorRead.HasValue ? (int)devMajorRead.Value : 0,
                    DevMinor = devMinorRead.HasValue ? (int)devMinorRead.Value : 0,
                    Prefix = string.IsNullOrEmpty(prefix) ? null : prefix,
                    TypeFlag = (char)type
                };

                // Wrap the region into a slice of the original stream
                yield return tarEntryStream;
            }
            else if (type == '5')
            {
                // Directory entry
                var unixTimeStamp = ReadOctal(header, 136, 12);
                var lastModifiedTime = unixTimeStamp.HasValue ? Epoch.AddSeconds(unixTimeStamp.Value).ToLocalTime() : DateTime.Now;

                // If ustar, load prefix filename
                if (magic == "ustar")
                {
                    fileName = prefix + fileName;
                }

                tarEntryStream = new TarEntryStream(inputStream, position, 0)
                {
                    FileName = fileName,
                    LastModifiedTime = lastModifiedTime,
                    IsDirectory = true,
                    Mode = modeRead.HasValue ? (int)modeRead.Value : 0,
                    UserId = uidRead.HasValue ? (int)uidRead.Value : 0,
                    GroupId = gidRead.HasValue ? (int)gidRead.Value : 0,
                    UserName = string.IsNullOrEmpty(userName) ? null : userName,
                    GroupName = string.IsNullOrEmpty(groupName) ? null : groupName,
                    LinkName = string.IsNullOrEmpty(linkName) ? null : linkName,
                    Magic = string.IsNullOrEmpty(magic) ? null : magic,
                    Version = string.IsNullOrEmpty(version) ? null : version,
                    DevMajor = devMajorRead.HasValue ? (int)devMajorRead.Value : 0,
                    DevMinor = devMinorRead.HasValue ? (int)devMinorRead.Value : 0,
                    Prefix = string.IsNullOrEmpty(prefix) ? null : prefix,
                    TypeFlag = (char)type
                };

                // For directories there is no data to read, just yield the entry
                yield return tarEntryStream;
            }
            else if (type == '2')
            {
                // Symbolic link
                var unixTimeStamp = ReadOctal(header, 136, 12);
                var lastModifiedTime = unixTimeStamp.HasValue ? Epoch.AddSeconds(unixTimeStamp.Value).ToLocalTime() : DateTime.Now;

                // If ustar, load prefix filename
                if (magic == "ustar")
                {
                    fileName = prefix + fileName;
                }

                tarEntryStream = new TarEntryStream(inputStream, position, 0)
                {
                    FileName = fileName,
                    LastModifiedTime = lastModifiedTime,
                    IsDirectory = false,
                    Mode = modeRead.HasValue ? (int)modeRead.Value : 0,
                    UserId = uidRead.HasValue ? (int)uidRead.Value : 0,
                    GroupId = gidRead.HasValue ? (int)gidRead.Value : 0,
                    UserName = string.IsNullOrEmpty(userName) ? null : userName,
                    GroupName = string.IsNullOrEmpty(groupName) ? null : groupName,
                    LinkName = string.IsNullOrEmpty(linkName) ? null : linkName,
                    Magic = string.IsNullOrEmpty(magic) ? null : magic,
                    Version = string.IsNullOrEmpty(version) ? null : version,
                    DevMajor = devMajorRead.HasValue ? (int)devMajorRead.Value : 0,
                    DevMinor = devMinorRead.HasValue ? (int)devMinorRead.Value : 0,
                    Prefix = string.IsNullOrEmpty(prefix) ? null : prefix,
                    TypeFlag = (char)type
                };

                // Symlink has no data payload in the header, just yield the entry
                yield return tarEntryStream;
            }

            // The end of the file entry is aligned on 512 bytes
            var untilPosition = (position + fileLength + 511) & ~511;
            if (tarEntryStream != null)
            {
                position += tarEntryStream.Position;
            }

            // We seek to untilPosition by reading the remaining bytes
            // as we don't want to rely on stream.Seek/Position as it is
            // not working with GzipStream for example
            int delta;
            while ((delta = (int)(untilPosition - position)) > 0)
            {
                delta = Math.Min(512, delta);
                var readCount = inputStream.Read(header, 0, delta);
                position += readCount;
                if (readCount == 0)
                {
                    break;
                }
            }

            // If we are not at target position, there is an error, so exit
            if ((untilPosition - position) != 0)
            {
                throw new InvalidDataException($"Invalid end of entry after file entry [{fileName}] ");
            }
        }
    }

    /// <summary>
    /// Gets an ASCII string ending by a `\0`
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="index">The index.</param>
    /// <param name="count">The count.</param>
    /// <returns>A string</returns>
    private static string GetString(byte[] buffer, int index, int count)
    {
        var text = new StringBuilder();
        for (int i = index; i < index + count; i++)
        {
            if (buffer[i] == 0 || buffer[i] >= 127)
            {
                break;
            }

            text.Append((char)buffer[i]);
        }
        return text.ToString();
    }

    /// <summary>
    /// Reads an octal number converted to integer.
    /// </summary>
    /// <param name="buffer">The buffer.</param>
    /// <param name="index">The index.</param>
    /// <param name="count">The count.</param>
    /// <returns>An octal number converted to a long; otherwise <c>null</c> if the conversion failed</returns>
    private static long? ReadOctal(byte[] buffer, int index, int count)
    {
        long value = 0;
        for (int i = index; i < index + count; i++)
        {
            var c = buffer[i];
            if (c == 0)
            {
                break;
            }
            if (c == ' ')
            {
                continue;
            }
            if (c < '0' || c > '7')
            {
                return null;
            }
            value = (value << 3) + (c - '0');
        }
        return value;
    }
}
