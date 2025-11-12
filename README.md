# NTar

A single file C# file to untar file entries from a `tar` stream.

This is a fork of [xoofx/NTar](https://github.com/xoofx/NTar).

## Usage

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

## Usage

Copy the [`TarHelper.cs`](http://github.com/xoofx/NTar/tree/master/src/NTar/TarHelper.cs) file directly into your project 

The code is compatible with `.NET 4.x+` and `.NETCore`

## Limitations

NTar does not provide currently creation of a tar archive.

## License
This software is released under the [BSD-Clause 2 license](http://opensource.org/licenses/BSD-2-Clause). 

## Author

Alexandre Mutel aka [xoofx](https://xoofx.github.io).
