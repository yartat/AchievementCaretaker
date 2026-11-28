/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using AchievementCaretaker.SteamApi;
using Xunit;

namespace AchievementCaretaker.Providers.Steam.Tests;

/// <summary>Steam's cache files are parsed by recursion, so how deep a file may nest has to be limited.</summary>
public sealed class KeyValueDepthTests
{
    private static MemoryStream Binary(int depth)
    {
        List<byte> bytes = new();
        for (var i = 0; i < depth; i++)
        {
            bytes.AddRange(new byte[] { (byte)KeyValueType.None, (byte)'a', 0 });
        }
        // One end for each level, and one for the root.
        bytes.AddRange(Enumerable.Repeat((byte)KeyValueType.End, depth + 1));
        return new MemoryStream(bytes.ToArray());
    }

    private static string Text(int depth) =>
        string.Concat(Enumerable.Repeat("\"a\" {\n", depth)) + string.Concat(Enumerable.Repeat("}\n", depth));

    [Fact]
    public void A_file_nested_as_deep_as_steams_are_is_read()
    {
        Assert.True(new KeyValue().ReadAsBinary(Binary(10)));
        Assert.True(TextKeyValue.Parse(Text(10))["a"]["a"].Valid);
    }

    [Theory]
    [InlineData(200)]
    [InlineData(10_000)]
    public void A_binary_file_nested_far_deeper_is_refused(int depth)
    {
        Assert.False(new KeyValue().ReadAsBinary(Binary(depth)));
    }

    [Theory]
    [InlineData(200)]
    [InlineData(10_000)]
    public void A_text_file_nested_far_deeper_is_refused(int depth)
    {
        Assert.Same(TextKeyValue.Invalid, TextKeyValue.Parse(Text(depth)));
    }
}
