/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Diagnostics.CodeAnalysis;
using System.Threading;
using System.Threading.Tasks;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;

namespace AchievementCaretaker.App.Library;

/// <summary>How far through a game's achievements the user is.</summary>
internal readonly record struct StoreProgress(int Earned, int Total);

/// <summary>
/// One game in an Epic or GOG library.
/// </summary>
/// <remarks>
/// Deliberately not <see cref="GameInfo"/>. That class, the cache table behind it,
/// the rating store and the capsule cache are all keyed by a Steam app id, and a
/// store's game id is a string. Store games have none of Steam's playtime, last
/// played, review rating or demo/mod types either. The members the tile and row
/// templates bind to are named as they are on <see cref="GameInfo"/> so the XAML
/// can be read side by side.
/// </remarks>
internal sealed partial class StoreGameInfo : ObservableObject
{
    private string _name = "";

    public string Provider { get; }

    public string Id { get; }

    /// <summary>What <c>IStoreProvider.GetAchievementsAsync</c> is called with.</summary>
    public string Key { get; }

    public string? ImageUrl { get; set; }

    /// <summary>
    /// Reads the capsule when the tile comes into view (<c>LazyImage</c>). The bitmap is not kept
    /// here: it belongs to the control showing it, so a library does not hold every capsule decoded.
    /// Set again when the image address changes, which is what makes the tile ask again.
    /// </summary>
    [ObservableProperty]
    private Func<CancellationToken, Task<Bitmap?>>? _logoLoader;

    /// <summary>The name to show; a blank one is replaced by the game's id.</summary>
    [AllowNull]
    public string Name
    {
        get => _name;
        set => SetProperty(ref _name, string.IsNullOrWhiteSpace(value) ? Id : value);
    }

    /// <summary>
    /// Null means "not read yet", which is deliberately distinct from "none
    /// earned": a store's achievements cost a request per game to find out.
    /// </summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(AchievementsText))]
    [NotifyPropertyChangedFor(nameof(Completion))]
    [NotifyPropertyChangedFor(nameof(HasCompletion))]
    [NotifyPropertyChangedFor(nameof(CompletionFraction))]
    [NotifyPropertyChangedFor(nameof(CompletionText))]
    [NotifyPropertyChangedFor(nameof(IsPerfect))]
    private StoreProgress? _progress;

    /// <summary>0..1, or null when the achievements have not been read or the game has none.</summary>
    public double? Completion
    {
        get
        {
            if (Progress.HasValue == false || Progress.Value.Total <= 0)
            {
                return null;
            }
            return Math.Clamp(Progress.Value.Earned / (double)Progress.Value.Total, 0.0, 1.0);
        }
    }

    public bool HasCompletion => Completion.HasValue;

    public double CompletionFraction => Completion ?? 0.0;

    public bool IsPerfect => Completion.HasValue == true && Completion.Value >= 1.0;

    public string CompletionText =>
        Completion.HasValue == false
            ? "Not loaded yet"
            : (Completion.Value * 100.0).ToString("0", CultureInfo.CurrentCulture) + "%";

    public string AchievementsText
    {
        get
        {
            if (Progress.HasValue == false)
            {
                return "—";
            }
            if (Progress.Value.Total == 0)
            {
                return "none";
            }
            return $"{Progress.Value.Earned} / {Progress.Value.Total}";
        }
    }

    public StoreGameInfo(string provider, string id, string key)
    {
        Provider = provider;
        Id = id;
        Key = key;
        Name = null;
    }
}
