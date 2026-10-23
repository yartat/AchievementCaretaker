/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Text.Json;
using Xunit;

namespace AchievementCaretaker.Providers.Tests;

/// <summary>
/// What the providers read out of the stores' answers. The stores are not consistent
/// about types and dates, so these helpers are lenient on purpose.
/// </summary>
public sealed class JsonHelpersTests
{
    private static JsonElement Parse(string json) => JsonDocument.Parse(json).RootElement.Clone();

    [Fact]
    public void TryWalk_follows_a_path_of_properties()
    {
        var root = Parse("{ \"a\": { \"b\": { \"c\": 7 } } }");

        Assert.True(JsonHelpers.TryWalk(root, out var found, "a", "b", "c"));
        Assert.Equal(7, found.GetInt32());
    }

    [Fact]
    public void TryWalk_fails_on_a_missing_step_and_on_a_non_object()
    {
        var root = Parse("{ \"a\": 5 }");

        Assert.False(JsonHelpers.TryWalk(root, out _, "a", "b"));
        Assert.False(JsonHelpers.TryWalk(root, out _, "missing"));
    }

    [Fact]
    public void GetString_returns_null_for_a_missing_or_non_string_value()
    {
        var root = Parse("{ \"s\": \"x\", \"n\": 1 }");

        Assert.Equal("x", JsonHelpers.GetString(root, "s"));
        Assert.Null(JsonHelpers.GetString(root, "n"));
        Assert.Null(JsonHelpers.GetString(root, "missing"));
    }

    [Fact]
    public void FirstOf_takes_the_first_name_that_has_a_value()
    {
        var root = Parse("{ \"b\": \"second\", \"c\": \"third\" }");

        Assert.Equal("second", JsonHelpers.FirstOf(root, "a", "b", "c"));
        Assert.Null(JsonHelpers.FirstOf(root, "x", "y"));
    }

    [Fact]
    public void GetBool_is_false_when_missing()
    {
        var root = Parse("{ \"t\": true, \"f\": false }");

        Assert.True(JsonHelpers.GetBool(root, "t"));
        Assert.False(JsonHelpers.GetBool(root, "f"));
        Assert.False(JsonHelpers.GetBool(root, "missing"));
    }

    [Fact]
    public void GetDouble_is_null_when_missing_not_zero()
    {
        var root = Parse("{ \"n\": 12.5, \"zero\": 0 }");

        Assert.Equal(12.5, JsonHelpers.GetDouble(root, "n"));
        Assert.Equal(0.0, JsonHelpers.GetDouble(root, "zero"));
        Assert.Null(JsonHelpers.GetDouble(root, "missing"));
    }

    [Theory]
    [InlineData("2024-03-05T10:20:30+0000", "2024-03-05T10:20:30+00:00")]
    [InlineData("2024-03-05T10:20:30-0700", "2024-03-05T10:20:30-07:00")]
    [InlineData("2024-03-05T10:20:30+0530", "2024-03-05T10:20:30+05:30")]
    [InlineData("2024-03-05T10:20:30Z", "2024-03-05T10:20:30+00:00")]
    [InlineData("2024-03-05T10:20:30+00:00", "2024-03-05T10:20:30+00:00")]
    public void GetDate_reads_the_offsets_the_stores_write(string text, string expected)
    {
        var root = Parse("{ \"d\": \"" + text + "\" }");

        Assert.Equal(DateTimeOffset.Parse(expected), JsonHelpers.GetDate(root, "d"));
    }

    [Theory]
    [InlineData("{ \"d\": null }")]
    [InlineData("{ \"d\": \"\" }")]
    [InlineData("{ \"d\": \"not a date\" }")]
    [InlineData("{ }")]
    public void GetDate_is_null_when_there_is_no_date(string json)
    {
        Assert.Null(JsonHelpers.GetDate(Parse(json), "d"));
    }
}
