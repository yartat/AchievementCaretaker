/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Xunit;

namespace AchievementCaretaker.Services.Tests;

/// <summary>The ratings are the one thing here that cannot be rebuilt, so what a bad file costs matters.</summary>
public sealed class RatingFileTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sac-ratings-" + Guid.NewGuid().ToString("N"));

    public RatingFileTests() => Directory.CreateDirectory(_directory);

    public void Dispose() => Directory.Delete(_directory, true);

    private string FilePath => Path.Combine(_directory, "ratings.json");

    [Fact]
    public void A_rating_is_there_after_a_reload()
    {
        Assert.True(RatingFile.Load(FilePath).Set(10, 1));

        Assert.Equal(1, RatingFile.Load(FilePath).Get(10));
        Assert.Equal(0, RatingFile.Load(FilePath).Get(11));
    }

    [Fact]
    public void A_file_that_cannot_be_read_is_kept_aside_before_anything_is_saved_over_it()
    {
        const string damaged = "{ \"10\": 1, \"20\": ";
        File.WriteAllText(FilePath, damaged);

        RatingFile.Load(FilePath).Set(30, 1);

        var copy = Assert.Single(Directory.GetFiles(_directory, "ratings.json.corrupt-*"));
        Assert.Equal(damaged, File.ReadAllText(copy));
    }

    [Fact]
    public async Task One_damaged_file_is_kept_aside_once_and_another_damage_beside_it()
    {
        File.WriteAllText(FilePath, "{ one");
        RatingFile.Load(FilePath);
        await Task.Delay(1100, TestContext.Current.CancellationToken);
        RatingFile.Load(FilePath);
        Assert.Single(Directory.GetFiles(_directory, "ratings.json.corrupt-*"));

        File.WriteAllText(FilePath, "{ two");
        RatingFile.Load(FilePath);

        Assert.Equal(2, Directory.GetFiles(_directory, "ratings.json.corrupt-*").Length);
    }

    // The old place is read when the file could not be moved; what changes goes to the new one.
    [Fact]
    public void Ratings_read_from_an_old_place_are_saved_to_the_new_one()
    {
        var old = Path.Combine(_directory, "old.json");
        RatingFile.Load(old).Set(10, 1);
        var before = File.ReadAllText(old);

        Assert.True(RatingFile.Load(old, FilePath).Set(20, 1));

        Assert.Equal(before, File.ReadAllText(old));
        Assert.Equal(1, RatingFile.Load(FilePath).Get(10));
        Assert.Equal(1, RatingFile.Load(FilePath).Get(20));
    }

    [Fact]
    public void A_missing_file_makes_no_copy()
    {
        RatingFile.Load(FilePath).Set(30, 1);

        Assert.Equal("ratings.json", Path.GetFileName(Directory.GetFiles(_directory).Single()));
    }

    [Fact]
    public void A_file_that_is_locked_is_not_overwritten()
    {
        RatingFile.Load(FilePath).Set(10, 1);
        RatingFile ratings;
        using (new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            ratings = RatingFile.Load(FilePath);
        }

        // The ratings could not be read, so saving would keep only the new one.
        Assert.False(ratings.Set(20, 1));
        Assert.Equal(1, RatingFile.Load(FilePath).Get(10));
    }
}
