namespace NTar;

/// <summary>
/// Enum representing the type of a tar entry, mapped from the header typeflag.
/// </summary>
public enum TarEntryType : byte
{
    File = (byte)'0',
    HardLink = (byte)'1',
    SymbolicLink = (byte)'2',
    CharacterDevice = (byte)'3',
    BlockDevice = (byte)'4',
    Directory = (byte)'5',
    FIFO = (byte)'6',
    Contiguous = (byte)'7',
    Unknown = byte.MaxValue,
}
