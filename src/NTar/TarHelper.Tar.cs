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
        var dir = Path.GetFullPath(inputDirectory);
        if (!Directory.Exists(dir)) throw new DirectoryNotFoundException(dir);

        // collect entries (directories first so that consumers can create directories before files)
        var entries = new List<TarEntryStream>();

        // Add directory entries
        foreach (var d in Directory.EnumerateDirectories(dir, "*", SearchOption.AllDirectories).Concat(new[] { dir }))
        {
            var rel = GetRelativePath(dir, d);
            // tar usually uses ./ prefix for relative paths, match Untar tests style
            if (!rel.StartsWith("./") && !rel.StartsWith("/")) rel = "./" + rel;
            var ms = new MemoryStream();
            var tes = new TarEntryStream(ms, 0, 0)
            {
                FileName = rel.EndsWith("/") ? rel : rel + "/",
                LastModifiedTime = Directory.GetLastWriteTime(d),
                IsDirectory = true,
                TypeFlag = '5'
            };
            entries.Add(tes);
        }

        // Add file entries
        foreach (var f in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            var rel = GetRelativePath(dir, f);
            if (!rel.StartsWith("./") && !rel.StartsWith("/")) rel = "./" + rel;
            var bytes = File.ReadAllBytes(f);
            var ms = new MemoryStream(bytes, writable: false);
            var tes = new TarEntryStream(ms, 0, bytes.LongLength)
            {
                FileName = rel,
                LastModifiedTime = File.GetLastWriteTime(f),
                IsDirectory = false,
                TypeFlag = '0'
            };
            entries.Add(tes);
        }

        using var tarStream = entries.Tar();

        var outputFile = Path.GetFullPath(outputFileName);
        var outDir = Path.GetDirectoryName(outputFile);
        if (!string.IsNullOrEmpty(outDir) && !Directory.Exists(outDir))
        {
            Directory.CreateDirectory(outDir);
        }

        using var fs = new FileStream(outputFile, FileMode.Create, FileAccess.Write);
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

        var ms = new MemoryStream();

        foreach (var entry in entries)
        {
            var name = entry.FileName ?? string.Empty;
            // Normalize to unix separators
            name = name.Replace(Path.DirectorySeparatorChar, '/');

            // Build header
            var header = new byte[512];

            // filename and prefix handling
            var nameBytes = Encoding.ASCII.GetBytes(name);
            if (nameBytes.Length <= 100)
            {
                Array.Copy(nameBytes, 0, header, 0, nameBytes.Length);
            }
            else
            {
                // try to split into prefix and name at slash
                var s = name;
                var splitPos = s.Length;
                while (splitPos > 0 && s.Substring(0, splitPos).Length > 155)
                {
                    splitPos = s.LastIndexOf('/', splitPos - 1);
                    if (splitPos == -1) break;
                }
                if (splitPos > 0)
                {
                    var prefix = s.Substring(0, splitPos);
                    var shortName = s.Substring(splitPos + 1);
                    var prefBytes = Encoding.ASCII.GetBytes(prefix);
                    var shortBytes = Encoding.ASCII.GetBytes(shortName);
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
            var mtime = (long)(entry.LastModifiedTime.ToUniversalTime() - Epoch).TotalSeconds;
            WriteOctal(header, 136, 12, mtime);

            // typeflag
            header[156] = (byte)entry.TypeFlag;

            // linkname - leave empty

            // magic and version
            var magic = Encoding.ASCII.GetBytes("ustar\0");
            Array.Copy(magic, 0, header, 257, magic.Length);
            var version = Encoding.ASCII.GetBytes("00");
            Array.Copy(version, 0, header, 263, version.Length);

            // uname/gname
            var uname = Encoding.ASCII.GetBytes(Environment.UserName ?? string.Empty);
            Array.Copy(uname, 0, header, 265, Math.Min(uname.Length, 32));
            var gname = Encoding.ASCII.GetBytes(string.Empty);
            Array.Copy(gname, 0, header, 297, 0);

            // devmajor/devminor left zero

            // prefix already handled

            // checksum placeholder: fill with spaces
            for (int i = 148; i < 156; i++) header[i] = 32;

            // compute checksum
            uint chksum = 0;
            for (int i = 0; i < 512; i++) chksum += header[i];

            // write checksum as 6-digit octal, then NUL and space
            var chks = Encoding.ASCII.GetBytes(Convert.ToString(chksum, 8).PadLeft(6, '0'));
            Array.Copy(chks, 0, header, 148, Math.Min(6, chks.Length));
            header[154] = 0;
            header[155] = 32;

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
                var pad = (int)((512 - (ms.Position % 512)) % 512);
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
        // Write value as octal ASCII, right aligned, with leading zeros/spaces as needed
        var oct = Convert.ToString(value < 0 ? 0 : value, 8);
        var octBytes = Encoding.ASCII.GetBytes(oct);
        var len = Math.Min(count - 1, octBytes.Length);
        var start = index + (count - 1 - len);
        Array.Copy(octBytes, octBytes.Length - len, buffer, start, len);
        // trailing NUL
        buffer[index + count - 1] = 0;
    }

    private static string GetRelativePath(string root, string path)
    {
        if (string.IsNullOrEmpty(root)) return "./";
        var fullRoot = Path.GetFullPath(root).TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
        var fullPath = Path.GetFullPath(path);

        // If path starts with root, return the relative portion
        if (fullPath.StartsWith(fullRoot, StringComparison.OrdinalIgnoreCase))
        {
            var rel = fullPath.Substring(fullRoot.Length).Replace(Path.DirectorySeparatorChar, '/');
            if (string.IsNullOrEmpty(rel)) return "./";
            return "./" + rel;
        }

        // Fallback: strip the drive/root and return a path relative to the drive root
        try
        {
            var rootPath = Path.GetPathRoot(fullPath) ?? string.Empty;
            var rel = fullPath.Substring(rootPath.Length).Replace(Path.DirectorySeparatorChar, '/');
            if (string.IsNullOrEmpty(rel)) return "./";
            return "./" + rel;
        }
        catch
        {
            return "./" + fullPath.Replace(Path.DirectorySeparatorChar, '/');
        }
    }
}
