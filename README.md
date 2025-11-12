# NTar

This small library provides minimal TAR archive reading and writing helpers.

This is a fork of [xoofx/NTar](https://github.com/xoofx/NTar).

APIs added
- `TarHelper.Untar.cs` — already present: extension methods to untar a `Stream` and to extract to disk: `Stream.Untar()` and `Stream.UntarTo(string outputDirectory)`.
- `TarHelper.Tar.cs` — new: create tar archives:
  - `public static void TarTo(this string inputDirectory)` — create a `.tar` file next to the specified input directory. Example: `"C:\my\folder".TarTo()` creates `C:\my\folder.tar`.
  - `public static Stream Tar(this IEnumerable<TarEntryStream> entries)` — build a tar archive in-memory from a sequence of `TarEntryStream` instances and return it as a seekable `Stream`.

## Usage examples

Create a tar file from a directory:

```csharp
// Creates "test.tar"
"C:\path\to\folder".TarTo("test.tar");
```

Create a tar stream from programmatic entries:

```csharp
var entries = new List<TarEntryStream>();
// create TarEntryStream objects (MemoryStream wrappers) and set FileName, LastModifiedTime, IsDirectory, etc.
Stream tar = entries.Tar();
// write tar to disk
using (var fs = File.Create("out.tar")) tar.CopyTo(fs);
```

Gets all files entries from a tar stream:

```C#
foreach (var entryStream in stream.Untar())
{
    // stream.FileName, stream.LastModifiedTime, stream.Length
}
```

Untar the stream to a specified output directory:

```C#
stream.UntarTo(".");
```

## Alternative

[mfow/tar-cs](https://github.com/mfow/tar-cs)

## License

This software is released under the [BSD-Clause 2 license](http://opensource.org/licenses/BSD-2-Clause).

## Authors

Alexandre Mutel aka [xoofx](https://xoofx.github.io).

And modify it to support the `tar` method used by ema.
