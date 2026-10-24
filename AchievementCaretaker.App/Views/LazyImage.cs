/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media.Imaging;

namespace AchievementCaretaker.App.Views;

/// <summary>
/// An image that is loaded when it comes into view and let go of when it leaves.
/// </summary>
/// <remarks>
/// A library is hundreds of capsules, and holding every decoded bitmap for the life of the window
/// was over a gigabyte. Here the bitmap belongs to the control that shows it: the
/// <see cref="Loader"/> is asked once the control is within <see cref="ViewMargin"/> pixels of the
/// scroll viewport, and the bitmap is disposed once it is outside it.
/// The loader reads the disk cache and falls back to the network; scrolling back finds the file
/// on disk, so nothing is decoded twice more than the viewer needs.
/// </remarks>
internal sealed class LazyImage : Image
{
    /// <summary>How far outside the viewport counts as in view, so the next row is ready before it scrolls in.</summary>
    private const double ViewMargin = 300;

    public static readonly StyledProperty<Func<CancellationToken, Task<Bitmap>>> LoaderProperty =
        AvaloniaProperty.Register<LazyImage, Func<CancellationToken, Task<Bitmap>>>(nameof(Loader));

    private CancellationTokenSource _load;
    private bool _inView;

    static LazyImage()
    {
        LoaderProperty.Changed.AddClassHandler<LazyImage>((image, _) => image.Reset());
    }

    /// <summary>Gives the bitmap, or null when there is none. Runs when the image comes into view.</summary>
    public Func<CancellationToken, Task<Bitmap>> Loader
    {
        get => GetValue(LoaderProperty);
        set => SetValue(LoaderProperty, value);
    }

    protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
    {
        base.OnAttachedToVisualTree(e);
        EffectiveViewportChanged += OnEffectiveViewportChanged;
    }

    protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
    {
        EffectiveViewportChanged -= OnEffectiveViewportChanged;
        _inView = false;
        Release();
        base.OnDetachedFromVisualTree(e);
    }

    private void OnEffectiveViewportChanged(object sender, EffectiveViewportChangedEventArgs e)
    {
        var area = new Rect(Bounds.Size);
        _inView = e.EffectiveViewport.Width > 0 &&
                  e.EffectiveViewport.Height > 0 &&
                  e.EffectiveViewport.Inflate(new Thickness(0, ViewMargin)).Intersects(area);
        Update();
    }

    /// <summary>The loader changed: what is shown belongs to the old one.</summary>
    private void Reset()
    {
        Release();
        Update();
    }

    private void Update()
    {
        if (_inView == false)
        {
            Release();
            return;
        }

        if (Source == null && _load == null && Loader != null)
        {
            Task load = LoadAsync(Loader);
            GC.KeepAlive(load);
        }
    }

    private async Task LoadAsync(Func<CancellationToken, Task<Bitmap>> loader)
    {
        CancellationTokenSource mine = new();
        _load = mine;
        try
        {
            var bitmap = await loader(mine.Token);

            // Left the view, or got another loader, while it was being read.
            if (mine.IsCancellationRequested == false)
            {
                Source = bitmap;
            }
            else
            {
                bitmap?.Dispose();
            }
        }
        catch (OperationCanceledException)
        {
            // Released before it finished.
        }
        catch (Exception)
        {
            // A missing capsule is not worth surfacing.
        }
        finally
        {
            if (ReferenceEquals(_load, mine) == true)
            {
                _load = null;
            }
            mine.Dispose();
        }
    }

    private void Release()
    {
        _load?.Cancel();
        _load = null;

        // The bitmap is this control's alone (every load makes a new one), and its pixels are
        // native memory the garbage collector cannot see: a few hundred bytes of managed object
        // for megabytes of pixels, so a collection may not come for a long time. Let go now.
        var old = Source as IDisposable;
        Source = null;
        old?.Dispose();
    }
}
