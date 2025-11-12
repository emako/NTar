[![GitHub license](https://img.shields.io/github/license/emako/NTar)](https://github.com/emako/NTar/blob/master/LICENSE.txt) [![NuGet](https://img.shields.io/nuget/v/NTar.svg)](https://www.nuget.org/packages/NTar) [![Actions](https://github.com/emako/NTar/actions/workflows/library.nuget.yml/badge.svg)](https://github.com/emako/NTar/actions/workflows/library.nuget.yml)

# NTar

NTar is a small .NET library for serializing/deserializing a tar file.

It exposes simple APIs to create TAR archives from directories or programmatic entries and to read/extract TAR archives from streams.

Key highlights

- Small, focused: reading and writing TAR without heavyweight dependencies.
- Supports a wide range of .NET targets (see Supported targets).
- Simple streaming APIs for programmatic creation and extraction.

Supported targets

The library provides builds and packages for multiple targets including (but not limited to):

- .NET Framework 4.5 and later
- .NET 5.0 and later
- .NET Standard 2.0 and later

## Installation

Install the package from NuGet:

```powershell
dotnet add package NTar
```

## Usage examples

Create a .tar file from a directory:

```csharp
// Creates "test.tar" (next to the input directory)
"C:\path\to\folder".TarTo("test.tar");
```

Create a tar stream from programmatic entries and write to disk:

```csharp
var entries = new List<TarEntryStream>();
// populate TarEntryStream objects (MemoryStream wrappers) and set FileName, LastModifiedTime, IsDirectory, etc.
using Stream tarStream = entries.Tar();
using var outFile = File.Create("out.tar");
tarStream.CopyTo(outFile);
```

Enumerate entries from a tar stream:

```csharp
foreach (var entry in myTarStream.Untar())
{
    Console.WriteLine(entry.FileName);
    // entry is a TarEntryStream-like object exposing FileName, LastModifiedTime, Length, and a Stream
}
```

Extract a tar stream to a directory:

```csharp
myTarStream.UntarTo("./output");
```

API notes

- Tar creation helpers are available in `TarHelper.Tar.cs` (e.g. `string.TarTo(string outputFile)` and `IEnumerable<TarEntryStream>.Tar()` returning a seekable `Stream`).
- Untar / extraction helpers are provided in `TarHelper.Untar.cs` as stream extension methods: `Stream.Untar()` and `Stream.UntarTo(string outputDirectory)`.

## License

This project is licensed under the [BSD-Clause 2 license](http://opensource.org/licenses/BSD-2-Clause).

## Acknowledgements

This project builds on work by Alexandre Mutel (aka xoofx).

If you need a different TAR implementation with more features, consider alternatives such as [mfow/tar-cs](https://github.com/mfow/tar-cs).

