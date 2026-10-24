/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Templates;
using Avalonia.Data;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.VisualTree;

namespace AchievementCaretaker.App.Views;

/// <summary>
/// A wrapping grid of fixed-size tiles that creates controls only for the tiles on or near the
/// screen.
/// </summary>
/// <remarks>
/// <para>
/// A <c>ListBox</c> over a <c>WrapPanel</c> builds a control tree for every item, and a library is
/// over a thousand tiles: 130 MB of managed objects and a slow first layout. This is an
/// <see cref="ItemsRepeater"/> with a <see cref="UniformGridLayout"/>, which needs every tile to be
/// the same size (<see cref="TileWidth"/> x <see cref="TileHeight"/>, set by the template's owner
/// so a tile with a hidden meter keeps its slot).
/// </para>
/// <para>
/// An <see cref="ItemsRepeater"/> selects nothing and moves nowhere, so this does what a
/// <c>ListBox</c> did: a press selects the tile under it (<see cref="SelectedItem"/>, two-way), the
/// selected tile gets the <c>selected</c> class (styled as <c>ContentControl.tile.selected</c>), and
/// the arrow, Home, End and Page keys move the selection and scroll it into view. Enter and a double
/// tap are not handled here: the owner subscribes to <c>KeyDown</c> and <c>DoubleTapped</c>.
/// </para>
/// </remarks>
internal sealed class TileGrid : ScrollViewer
{
    public static readonly StyledProperty<IEnumerable> ItemsSourceProperty =
        AvaloniaProperty.Register<TileGrid, IEnumerable>(nameof(ItemsSource));

    public static readonly StyledProperty<IDataTemplate> ItemTemplateProperty =
        AvaloniaProperty.Register<TileGrid, IDataTemplate>(nameof(ItemTemplate));

    public static readonly StyledProperty<object> SelectedItemProperty =
        AvaloniaProperty.Register<TileGrid, object>(nameof(SelectedItem), defaultBindingMode: BindingMode.TwoWay);

    public static readonly StyledProperty<double> TileWidthProperty =
        AvaloniaProperty.Register<TileGrid, double>(nameof(TileWidth), 211);

    public static readonly StyledProperty<double> TileHeightProperty =
        AvaloniaProperty.Register<TileGrid, double>(nameof(TileHeight), 158);

    private readonly ItemsRepeater _repeater = new();
    private readonly UniformGridLayout _layout = new()
    {
        Orientation = Orientation.Horizontal,
        ItemsStretch = UniformGridLayoutItemsStretch.None,
    };

    static TileGrid()
    {
        ItemsSourceProperty.Changed.AddClassHandler<TileGrid>((grid, e) => grid._repeater.ItemsSource = e.NewValue as IEnumerable);
        SelectedItemProperty.Changed.AddClassHandler<TileGrid>((grid, _) => grid.UpdateSelection());
        TileWidthProperty.Changed.AddClassHandler<TileGrid>((grid, _) => grid.UpdateTileSize());
        TileHeightProperty.Changed.AddClassHandler<TileGrid>((grid, _) => grid.UpdateTileSize());
    }

    public TileGrid()
    {
        HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled;
        Focusable = true;

        _repeater.Layout = _layout;
        _repeater.ItemTemplate = new FuncDataTemplate<object>(
            (item, _) => Build(item),
            supportsRecycling: false);
        _repeater.ElementPrepared += (_, e) => Mark(e.Element);
        Content = _repeater;
        UpdateTileSize();
    }

    // The theme's template is found by exact type, so without this a TileGrid has none: no
    // presenter, no scroll bar, and an extent of zero.
    protected override Type StyleKeyOverride => typeof(ScrollViewer);

    public IEnumerable ItemsSource
    {
        get => GetValue(ItemsSourceProperty);
        set => SetValue(ItemsSourceProperty, value);
    }

    /// <summary>How one item looks. Its root is placed in a tile of <see cref="TileWidth"/> x <see cref="TileHeight"/>.</summary>
    public IDataTemplate ItemTemplate
    {
        get => GetValue(ItemTemplateProperty);
        set => SetValue(ItemTemplateProperty, value);
    }

    public object SelectedItem
    {
        get => GetValue(SelectedItemProperty);
        set => SetValue(SelectedItemProperty, value);
    }

    /// <summary>Width of one tile including the space around it.</summary>
    public double TileWidth
    {
        get => GetValue(TileWidthProperty);
        set => SetValue(TileWidthProperty, value);
    }

    /// <summary>Height of one tile including the space around it.</summary>
    public double TileHeight
    {
        get => GetValue(TileHeightProperty);
        set => SetValue(TileHeightProperty, value);
    }

    private Control Build(object item)
    {
        // The content is set in Mark, not here: the repeater reuses elements and only gives them a
        // new DataContext, so what was put here would stay and show the wrong game.
        ContentControl tile = new()
        {
            ContentTemplate = ItemTemplate,
            Width = TileWidth,
            Height = TileHeight,
        };
        tile.Classes.Add("tile");
        return tile;
    }

    private void UpdateTileSize()
    {
        _layout.MinItemWidth = TileWidth;
        _layout.MinItemHeight = TileHeight;
    }

    private void Mark(Control element)
    {
        if (element is ContentControl tile)
        {
            tile.Content = tile.DataContext;
            tile.Width = TileWidth;
            tile.Height = TileHeight;
            tile.Classes.Set("selected", ReferenceEquals(tile.Content, SelectedItem) == true && SelectedItem != null);
        }
    }

    private void UpdateSelection()
    {
        foreach (var child in _repeater.Children)
        {
            Mark(child);
        }
    }

    protected override void OnPointerPressed(PointerPressedEventArgs e)
    {
        base.OnPointerPressed(e);

        Focus();
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed == false)
        {
            return;
        }

        // The tile the press landed in, if it landed in one and not on the scroll bar.
        for (var visual = e.Source as Visual; visual != null && ReferenceEquals(visual, this) == false; visual = visual.GetVisualParent())
        {
            if (visual is ContentControl { Content: { } item } tile && ReferenceEquals(tile.Parent, _repeater) == true)
            {
                SelectedItem = item;
                return;
            }
        }
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled == true || ItemsSource is not IList items || items.Count == 0)
        {
            return;
        }

        var columns = Columns;
        var rows = Math.Max(1, (int)(Viewport.Height / TileHeight));
        var current = SelectedItem == null ? -1 : items.IndexOf(SelectedItem);

        int target;
        switch (e.Key)
        {
            case Key.Left: target = current - 1; break;
            case Key.Right: target = current + 1; break;
            case Key.Up: target = current - columns; break;
            case Key.Down: target = current + columns; break;
            case Key.PageUp: target = current - columns * rows; break;
            case Key.PageDown: target = current + columns * rows; break;
            case Key.Home: target = 0; break;
            case Key.End: target = items.Count - 1; break;
            default: return;
        }

        // Nothing selected yet: the first key press lands on the first tile.
        if (current < 0)
        {
            target = 0;
        }
        target = Math.Clamp(target, 0, items.Count - 1);

        SelectedItem = items[target];
        ScrollToRow(target / columns);
        e.Handled = true;
    }

    /// <summary>How many tiles fit across, as the layout counts them.</summary>
    private int Columns => Math.Max(1, (int)(_repeater.Bounds.Width / TileWidth));

    /// <summary>
    /// Scrolls just far enough to show the whole row. The tiles are all one size, so where a row is
    /// is arithmetic; asking the element to bring itself into view does nothing until it has been
    /// arranged, which for a tile far off screen is not yet.
    /// </summary>
    private void ScrollToRow(int row)
    {
        var top = row * TileHeight;
        var bottom = top + TileHeight;
        if (top < Offset.Y)
        {
            Offset = new Vector(Offset.X, top);
        }
        else if (bottom > Offset.Y + Viewport.Height)
        {
            Offset = new Vector(Offset.X, bottom - Viewport.Height);
        }
    }
}
