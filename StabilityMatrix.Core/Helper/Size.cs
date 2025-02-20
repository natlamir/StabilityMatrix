﻿using System.Diagnostics.CodeAnalysis;

namespace StabilityMatrix.Core.Helper;

[SuppressMessage("ReSharper", "MemberCanBePrivate.Global")]
public static class Size
{
    public const ulong KiB = 1024;
    public const ulong MiB = KiB * 1024;
    public const ulong GiB = MiB * 1024;

    public static string FormatBytes(ulong bytes)
    {
        return bytes switch
        {
            < KiB => $"{bytes} B",
            < MiB => $"{bytes / (double)KiB:0.0} KiB",
            < GiB => $"{bytes / (double)MiB:0.0} MiB",
            _ => $"{bytes / (double)GiB:0.0} GiB"
        };
    }

    public static string FormatBase10Bytes(ulong bytes)
    {
        return bytes switch
        {
            < KiB => $"{bytes} Bytes",
            < MiB => $"{bytes / (double)KiB:0.0} KB",
            < GiB => $"{bytes / (double)MiB:0.0} MB",
            _ => $"{bytes / (double)GiB:0.00} GB"
        };
    }

    public static string FormatBase10Bytes(long bytes)
    {
        return FormatBase10Bytes(Convert.ToUInt64(bytes));
    }
}
