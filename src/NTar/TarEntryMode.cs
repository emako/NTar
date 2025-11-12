using System;

namespace NTar;

/// <summary>
/// Represents POSIX-style permission bits for entries in a tar archive.
/// Each flag corresponds to a single permission bit used in tar headers (mode field).
/// The values follow the conventional octal layout: owner (user), group, others.
/// </summary>
[Flags]
public enum TarEntryMode : int
{
    /// <summary>
    /// No permission bits set.
    /// </summary>
    None = 0,

    // Owner (user) permissions
    /// <summary>
    /// Owner (user) read permission (octal0400, decimal256).
    /// </summary>
    OwnerRead = 0b100_000_000, // 256

    /// <summary>
    /// Owner (user) write permission (octal0200, decimal128).
    /// </summary>
    OwnerWrite = 0b010_000_000, // 128

    /// <summary>
    /// Owner (user) execute permission (octal0100, decimal64).
    /// </summary>
    OwnerExecute = 0b001_000_000, // 64

    // Group permissions
    /// <summary>
    /// Group read permission (octal0040, decimal32).
    /// </summary>
    GroupRead = 0b000_100_000, // 32

    /// <summary>
    /// Group write permission (octal0020, decimal16).
    /// </summary>
    GroupWrite = 0b000_010_000, // 16

    /// <summary>
    /// Group execute permission (octal0010, decimal8).
    /// </summary>
    GroupExecute = 0b000_001_000, // 8

    // Others (world) permissions
    /// <summary>
    /// Others (world) read permission (octal0004, decimal4).
    /// </summary>
    OthersRead = 0b000_000_100, // 4

    /// <summary>
    /// Others (world) write permission (octal0002, decimal2).
    /// </summary>
    OthersWrite = 0b000_000_010, // 2

    /// <summary>
    /// Others (world) execute permission (octal0001, decimal1).
    /// </summary>
    OthersExecute = 0b000_000_001, // 1

    // Common combinations
    /// <summary>
    /// Read permission for owner, group and others (e.g. octal0444).
    /// </summary>
    AllRead = OwnerRead | GroupRead | OthersRead, // 292

    /// <summary>
    /// Write permission for owner, group and others (e.g. octal0222).
    /// </summary>
    AllWrite = OwnerWrite | GroupWrite | OthersWrite, // 146

    /// <summary>
    /// Execute permission for owner, group and others (e.g. octal0111).
    /// </summary>
    AllExecute = OwnerExecute | GroupExecute | OthersExecute, // 73

    /// <summary>
    /// All read, write and execute permissions for owner, group and others (octal0777, decimal511).
    /// </summary>
    Full = OwnerRead | OwnerWrite | OwnerExecute |
        GroupRead | GroupWrite | GroupExecute |
        OthersRead | OthersWrite | OthersExecute, // 511
}
