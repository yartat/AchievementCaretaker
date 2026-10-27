/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This project is based on Steam Achievement Manager (SAM)
 * Copyright (c) 2008-2024 Rick (rick 'at' gibbed 'dot' us)
 * https://github.com/gibbed/SteamAchievementManager
 *
 * This is an altered source version of that software, plainly marked as such.
 *
 * This software is provided 'as-is', without any express or implied
 * warranty. In no event will the authors be held liable for any damages
 * arising from the use of this software.
 *
 * Permission is granted to anyone to use this software for any purpose,
 * including commercial applications, and to alter it and redistribute it
 * freely, subject to the following restrictions:
 *
 * 1. The origin of this software must not be misrepresented; you must not
 *    claim that you wrote the original software. If you use this software
 *    in a product, an acknowledgment in the product documentation would
 *    be appreciated but is not required.
 *
 * 2. Altered source versions must be plainly marked as such, and must not
 *    be misrepresented as being the original software.
 *
 * 3. This notice may not be removed or altered from any source
 *    distribution.
 */

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using AchievementCaretaker.Providers.Steam;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using AchievementCaretaker.Providers.Steam.Models;

namespace AchievementCaretaker.App.Library;

internal sealed partial class GameInfo : ObservableObject
{
    private string _name = "";

    public uint Id { get; }
    public string Type { get; }
    public string? ImageUrl { get; set; }

    /// <summary>
    /// Reads the capsule when the tile comes into view (<c>LazyImage</c>). The bitmap is not kept
    /// here: it belongs to the control showing it, so a library does not hold every capsule decoded.
    /// Set again when the image address changes, which is what makes the tile ask again.
    /// </summary>
    [ObservableProperty]
    private Func<CancellationToken, Task<Bitmap?>>? _logoLoader;

    /// <summary>The name to show; null is replaced by <c>App &lt;id&gt;</c>.</summary>
    [AllowNull]
    public string Name
    {
        get => _name;
        set => SetProperty(
            ref _name,
            value ?? "App " + Id.ToString(CultureInfo.InvariantCulture));
    }

    /// <summary>
    /// Filled in from Steam's on-disk caches by <see cref="LibraryStats"/>.
    /// Null means "not known" — Steam only writes those caches for games
    /// that have been launched — which is deliberately distinct from zero.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(PlaytimeText))]
    [NotifyPropertyChangedFor(nameof(AchievementsText))]
    [NotifyPropertyChangedFor(nameof(LastPlayedText))]
    [NotifyPropertyChangedFor(nameof(Completion))]
    [NotifyPropertyChangedFor(nameof(HasCompletion))]
    [NotifyPropertyChangedFor(nameof(CompletionFraction))]
    [NotifyPropertyChangedFor(nameof(CompletionText))]
    [NotifyPropertyChangedFor(nameof(IsPerfect))]
    private GameStats? _stats;

    /// <summary>Steam's community review score as a percentage positive.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamRatingText))]
    [NotifyPropertyChangedFor(nameof(SteamRatingTooltip))]
    private int? _steamRatingPercent;

    /// <summary>Steam's 1-9 review band, used for the descriptive tooltip.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(SteamRatingTooltip))]
    private int? _steamRatingScore;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ReleaseDateText))]
    private DateTime? _releaseDate;

    /// <summary>
    /// The user's own like/dislike. Stored locally; see <see cref="OwnRating"/>.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(IsLiked))]
    [NotifyPropertyChangedFor(nameof(IsDisliked))]
    private OwnRating _ownRating;

    public bool IsLiked => OwnRating == OwnRating.Like;
    public bool IsDisliked => OwnRating == OwnRating.Dislike;

    public DateTime? LastPlayed => Stats?.LastPlayed;

    public string LastPlayedText =>
        Stats.HasValue == true && Stats.Value.LastPlayed.HasValue == true
            ? Stats.Value.LastPlayed.Value.ToString("d MMM yyyy", CultureInfo.CurrentCulture)
            : "—";

    public string ReleaseDateText =>
        ReleaseDate.HasValue == true
            ? ReleaseDate.Value.ToString("d MMM yyyy", CultureInfo.CurrentCulture)
            : "—";

    public string SteamRatingText =>
        SteamRatingPercent.HasValue == true
            ? SteamRatingPercent.Value.ToString(CultureInfo.CurrentCulture) + "%"
            : "—";

    public string SteamRatingTooltip
    {
        get
        {
            if (SteamRatingPercent.HasValue == false)
            {
                return "No Steam review data cached for this game";
            }
            var band = DescribeScore(SteamRatingScore);
            return band == null
                ? $"{SteamRatingPercent.Value}% of Steam reviews are positive"
                : $"{band} — {SteamRatingPercent.Value}% of Steam reviews are positive";
        }
    }

    /// <summary>Steam's published mapping for its 1-9 review score band.</summary>
    private static string? DescribeScore(int? score) => score switch
    {
        9 => "Overwhelmingly Positive",
        8 => "Very Positive",
        7 => "Positive",
        6 => "Mostly Positive",
        5 => "Mixed",
        4 => "Mostly Negative",
        3 => "Negative",
        2 => "Very Negative",
        1 => "Overwhelmingly Negative",
        _ => null,
    };

    public string PlaytimeText
    {
        get
        {
            if (Stats.HasValue == false)
            {
                return "—";
            }
            var minutes = Stats.Value.PlaytimeMinutes;
            if (minutes <= 0)
            {
                return "never played";
            }
            var hours = minutes / 60.0;
            return hours < 10
                ? hours.ToString("0.0", CultureInfo.CurrentCulture) + " h"
                : Math.Round(hours).ToString("0", CultureInfo.CurrentCulture) + " h";
        }
    }

    /// <summary>
    /// How far through this game's achievements the user is, 0..1, or null
    /// when Steam has never cached a schema for it. The Library grid draws
    /// a meter from this, and an empty meter has to mean "not known" rather
    /// than "none earned".
    /// </summary>
    public double? Completion
    {
        get
        {
            if (Stats.HasValue == false || Stats.Value.AchievementsTotal <= 0)
            {
                return null;
            }
            return Math.Clamp(
                Stats.Value.AchievementsEarned / (double)Stats.Value.AchievementsTotal,
                0.0,
                1.0);
        }
    }

    public bool HasCompletion => Completion.HasValue;

    /// <summary>Width of the meter's filled part, as a fraction of the track.</summary>
    public double CompletionFraction => Completion ?? 0.0;

    public bool IsPerfect => Completion.HasValue == true && Completion.Value >= 1.0;

    public string CompletionText =>
        Completion.HasValue == false
            ? "Not cached yet"
            : (Completion.Value * 100.0).ToString("0", CultureInfo.CurrentCulture) + "%";

    public string AchievementsText
    {
        get
        {
            if (Stats.HasValue == false || Stats.Value.AchievementsTotal < 0)
            {
                return "—";
            }
            if (Stats.Value.AchievementsTotal == 0)
            {
                return "none";
            }
            return $"{Stats.Value.AchievementsEarned} / {Stats.Value.AchievementsTotal}";
        }
    }

    public GameInfo(uint id, string type)
    {
        Id = id;
        Type = type;
        Name = null;
        ImageUrl = null;
    }
}
