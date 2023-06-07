// Copyright 2023 Subatomix Research Inc.
// SPDX-License-Identifier: ISC

namespace PSql;

internal static class StringExtensions
{
    internal static bool HasContent(this string? s)
        => !string.IsNullOrEmpty(s);

    internal static string? NullIfEmpty(this string? s)
        => string.IsNullOrEmpty(s) ? null : s;
}
