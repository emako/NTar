using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace NTar;

/// <summary>
/// An helper class to tar the tar-entries.
/// </summary>
public static partial class TarHelper
{
    /// <summary>
    /// Create a tar archive from a directory and write it to a file next to the directory with extension .tar
    /// </summary>
    /// <param name="inputDirectory">The input directory to archive.</param>
    /// <param name="outputFileName"></param>
    /// <exception cref="ArgumentNullException"></exception>
    /// <exception cref="DirectoryNotFoundException"></exception>
    public static void TarTo(this string inputDirectory, string outputFileName)
    {
        if (inputDirectory == null) throw new ArgumentNullException(nameof(inputDirectory));
        if (outputFileName == null) throw new ArgumentNullException(nameof(outputFileName));
        string dir = Path.GetFullPath(inputDirectory);
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException(dir);

        // collect entries (directories first so that consumers can create directories before files)
        List<TarEntryStream> entries = [];

        // Add directory entries
        foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories))
        {
            string rel = GetRelativePath(dir, d);
            MemoryStream ms = new();
            TarEntryStream tes = new(ms, 0, 0)
            {
                FileName = rel.EndsWith("/") ? rel : rel + "/",
                LastModifiedTime = Directory.GetLastWriteTime(d),
                IsDirectory = true,
                Type = TarEntryType.Directory,
            };

            entries.Add(tes);
        }

        // Add file entries
        foreach (string f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            string rel = GetRelativePath(dir, f);
            byte[] bytes = File.ReadAllBytes(f);
            MemoryStream ms = new(bytes, writable: false);
            TarEntryStream tes = new(ms, 0, bytes.LongLength)
            {
                FileName = rel,
                LastModifiedTime = File.GetLastWriteTime(f),
                IsDirectory = false,
                Type = TarEntryType.File,
            };

            entries.Add(tes);
        }

        using Stream tarStream = entries.Tar();
        string outputFile = Path.GetFullPath(outputFileName);
        string outDir = Path.GetDirectoryName(outputFile);

        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        using FileStream fs = new(outputFile, FileMode.Create, FileAccess.Write);
        tarStream.CopyTo(fs);
    }

    /// <summary>
    /// Build a tar archive stream from a set of entries. The returned stream is seekable and positioned at 0.
    /// </summary>
    /// <param name="entries">The entries to include in the tar.</param>
    /// <returns>A stream containing the tar archive.</returns>
    public static Stream Tar(this IEnumerable<TarEntryStream> entries)
    {
        if (entries == null) throw new ArgumentNullException(nameof(entries));

        MemoryStream ms = new();

        foreach (TarEntryStream entry in entries)
        {
            string name = entry.FileName ?? string.Empty;
            // Normalize to unix separators
            name = name.Replace(Path.DirectorySeparatorChar, '/');

            // Build header
            byte[] header = new byte[512];

            // filename and prefix handling
            byte[] nameBytes = Encoding.ASCII.GetBytes(name);
            if (nameBytes.Length <= 100)
            {
                Array.Copy(nameBytes, 0, header, 0, nameBytes.Length);
            }
            else
            {
                // try to split into prefix and name at slash
                string s = name;
                int splitPos = s.Length;
                while (splitPos > 0 && s.Substring(0, splitPos).Length > 155)
                {
                    splitPos = s.LastIndexOf('/', splitPos - 1);
                    if (splitPos == -1) break;
                }
                if (splitPos > 0)
                {
                    string prefix = s.Substring(0, splitPos);
                    string shortName = s.Substring(splitPos + 1);
                    byte[] prefBytes = Encoding.ASCII.GetBytes(prefix);
                    byte[] shortBytes = Encoding.ASCII.GetBytes(shortName);

                    Array.Copy(shortBytes, 0, header, 0, Math.Min(100, shortBytes.Length));
                    Array.Copy(prefBytes, 0, header, 345, Math.Min(155, prefBytes.Length));
                }
                else
                {
                    // fallback: truncate
                    Array.Copy(nameBytes, 0, header, 0, 100);
                }
            }

            // mode, uid, gid
            WriteOctal(header, 100, 8, entry.Mode);
            WriteOctal(header, 108, 8, entry.UserId);
            WriteOctal(header, 116, 8, entry.GroupId);

            // size
            WriteOctal(header, 124, 12, entry.IsDirectory ? 0 : entry.Length);

            // mtime (seconds since epoch)
            long mtime = (long)(entry.LastModifiedTime.ToUniversalTime() - Epoch).TotalSeconds;
            WriteOctal(header, 136, 12, mtime);

            // typeflag
            header[156] = (byte)entry.TypeFlag;

            // linkname - leave empty

            // magic and version
            byte[] magic = Encoding.ASCII.GetBytes("ustar\0");
            Array.Copy(magic, 0, header, 257, magic.Length);
            byte[] version = Encoding.ASCII.GetBytes("00");
            Array.Copy(version, 0, header, 263, version.Length);

            // uname/gname
            byte[] uname = Encoding.ASCII.GetBytes(Environment.UserName ?? string.Empty);
            Array.Copy(uname, 0, header, 265, Math.Min(uname.Length, 32));
            byte[] gname = Encoding.ASCII.GetBytes(string.Empty);
            Array.Copy(gname, 0, header, 297, 0);

            // devmajor/devminor left zero

            // prefix already handled

            // checksum placeholder: fill with spaces
            for (int i = 148; i < 156; i++) header[i] = 32;

            // compute checksum
            uint chksum = 0;
            for (int i = 0; i < 512; i++) chksum += header[i];

            // write checksum as 6-digit octal, then NUL and space
            byte[] chks = Encoding.ASCII.GetBytes(Convert.ToString(chksum, 8).PadLeft(6, '0'));
            Array.Copy(chks, 0, header, 148, Math.Min(6, chks.Length));
            header[154] = 0;
            header[155] = 32;

            // Sanity-check: recompute checksum as Untar does (treat checksum field as spaces)
            uint verify = 0;
            for (int i = 0; i < 512; i++)
            {
                var c = header[i];
                if (i >= 148 && i < (148 + 8)) c = 32;
                verify += c;
            }
            // verify should match; if it doesn't, something is wrong with WriteOctal
            if (verify != chksum)
            {
                throw new InvalidDataException($"Checksum computed mismatch when creating header: computed={chksum} verify={verify}");
            }

            // write header
            ms.Write(header, 0, 512);

            // write data
            if (!entry.IsDirectory && entry.Length > 0)
            {
                // Ensure the entry stream is at its start
                if (entry.CanSeek)
                    entry.Position = 0;

                // copy content
                entry.CopyTo(ms);
                // pad to 512
                int pad = (int)((512 - (ms.Position % 512)) % 512);
                if (pad > 0)
                {
                    ms.Write(new byte[pad], 0, pad);
                }
            }
        }

        // end with two 512 zero blocks
        ms.Write(new byte[1024], 0, 1024);
        ms.Position = 0;
        return ms;
    }

    private static void WriteOctal(byte[] buffer, int index, int count, long value)
    {
        // Write value as octal ASCII, right aligned, with leading spaces as padding and a trailing NUL
        string oct = Convert.ToString(value < 0 ? 0 : value, 8);
        byte[] octBytes = Encoding.ASCII.GetBytes(oct);
        int len = Math.Min(count - 1, octBytes.Length);

        // Fill the field with spaces (ASCII 32) so ReadOctal can skip leading spaces.
        for (int i = index; i < index + count - 1; i++) buffer[i] = 32;

        int start = index + (count - 1 - len);
        Array.Copy(octBytes, octBytes.Length - len, buffer, start, len);

        // trailing NUL
        buffer[index + count - 1] = 0;
    }

    private static string GetRelativePath(string root, string path)
    {
        if (string.IsNullOrEmpty(root)) return string.Empty;
        string fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        string fullPath = Path.GetFullPath(path);

        // If path starts with root, return the relative portion
        if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            string rel = fullPath.Substring(fullRoot.Length).Replace(Path.DirectorySeparatorChar, '/');
            if (string.IsNullOrEmpty(rel)) return string.Empty;
            return rel;
        }

        // Fallback: strip the drive/root and return a path relative to the drive root
        try
        {
            string rootPath = Path.GetPathRoot(fullPath) ?? string.Empty;
            string rel = fullPath.Substring(rootPath.Length).Replace(Path.DirectorySeparatorChar, '/');
            if (string.IsNullOrEmpty(rel)) return string.Empty;
            return rel;
        }
        catch
        {
            return fullPath.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
