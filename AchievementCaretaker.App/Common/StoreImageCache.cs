/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace AchievementCaretaker.App.Common;

/// <summary>
/// On-disk cache of images downloaded for Epic and GOG: capsules and
/// achievement icons.
/// </summary>
/// <remarks>
/// <para>
/// <see cref="IconCache"/> names every file after a Steam app id, and its
/// <c>Prune</c> recovers the id from the name. A store has no app ids, so its
/// images live in a <c>store/&lt;provider&gt;/</c> subfolder of the same
/// directory, named by a hash of the URL. That needs no name parsing, a changed
/// URL is simply a new file, and <c>IconCache.Prune</c>, which only walks the
/// top level, never sees them.
/// </para>
/// <para>
/// Because it follows the icon cache's directory, <see cref="IconCache.MoveTo"/>
/// has to carry the subfolder along; it does. Nothing prunes these files, since
/// a hash cannot be traced back to a game.
/// </para>
/// </remarks>
internal sealed class StoreImageCache
{
    public const string FolderName = "store";

    /// <summary>
    /// How many images are read and decoded at once. Scrolling a library fast asks for dozens, and
    /// each decode of a 4000-pixel capsule allocates tens of megabytes before it is scaled down:
    /// unbounded, that was a 500 MB spike on top of the 300 MB the window needs.
    /// </summary>
    private const int DecodeConcurrency = 3;

    private readonly SemaphoreSlim _decodeGate = new(DecodeConcurrency);

    private readonly IHttpClientFactory _http;

    // Read each time: IconCache.MoveTo changes it when the user relocates the cache.
    private readonly IconCache _icons;

    public StoreImageCache(IconCache icons, IHttpClientFactory http)
    {
        _icons = icons;
        _http = http;
    }

    private string FileFor(string provider, string url)
    {
        var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(url)))[..32];
        return Path.Combine(_icons.Path, FolderName, provider, hash + ".img");
    }

    /// <summary>
    /// Opens the cached file, or returns null when the image is not cached. A stream and not the
    /// bytes: a store's capsule is megabytes, so reading hundreds into arrays fills the large object
    /// heap with garbage the collector takes its time over (562 MB of it after scrolling one list).
    /// </summary>
    private Stream TryOpen(string provider, string url)
    {
        if (string.IsNullOrEmpty(url) == true)
        {
            return null;
        }
        try
        {
            var file = FileFor(provider, url);
            return File.Exists(file) == true
                ? new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan)
                : null;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// The image at <paramref name="url"/>, from disk if it is there and from the
    /// network if not (and then kept). Null when it could not be had: a missing
    /// capsule is not worth an error.
    /// </summary>
    /// <remarks>
    /// Reading and decoding happen off the calling thread, because a library is
    /// hundreds of images and decoding them on the UI thread stalls the window.
    /// The caller assigns the result, on the UI thread.
    /// </remarks>
    /// <param name="decodeWidth">
    /// Decode no wider than this, or null for the image as it is. A store's capsule art can be
    /// 4000 pixels wide (some 900 of them were 8.6 GB decoded), and it is shown a few hundred
    /// wide; small icons need no such limit.
    /// </param>
    public async Task<Bitmap> LoadAsync(string provider, string url, CancellationToken cancellationToken, int? decodeWidth = null)
    {
        if (string.IsNullOrEmpty(url) == true)
        {
            return null;
        }

        var cached = await DecodeGatedAsync(() => TryOpen(provider, url), decodeWidth, cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        try
        {
            var data = await _http.CreateClient().GetByteArrayAsync(new Uri(url), cancellationToken);
            var bitmap = await DecodeGatedAsync(() => new MemoryStream(data, false), decodeWidth, cancellationToken);
            if (bitmap != null)
            {
                await Task.Run(() => Write(provider, url, data), cancellationToken);
            }
            return bitmap;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads and decodes off the calling thread, at most <see cref="DecodeConcurrency"/> at a time. A
    /// caller that is cancelled while it waits for its turn (a tile that scrolled out of view) never
    /// reads or decodes anything.
    /// </summary>
    private async Task<Bitmap> DecodeGatedAsync(Func<Stream> open, int? decodeWidth, CancellationToken cancellationToken)
    {
        await _decodeGate.WaitAsync(cancellationToken);
        try
        {
            return await Task.Run(() => Decode(open(), decodeWidth), cancellationToken);
        }
        finally
        {
            _decodeGate.Release();
        }
    }

    /// <summary>Null for no stream, and for data that is not an image: a truncated file means a re-download. Disposes the stream.</summary>
    private static Bitmap Decode(Stream source, int? decodeWidth)
    {
        if (source == null)
        {
            return null;
        }
        try
        {
            using var stream = source;
            // Avalonia's Bitmap copies the decoded pixels, so the stream can go.
            return decodeWidth is int width ? Bitmap.DecodeToWidth(stream, width) : new Bitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }

    public void Write(string provider, string url, byte[] data)
    {
        if (string.IsNullOrEmpty(url) == true)
        {
            return;
        }
        try
        {
            var file = FileFor(provider, url);
            Directory.CreateDirectory(Path.GetDirectoryName(file));
            File.WriteAllBytes(file, data);
        }
        catch (Exception)
        {
            // A cache that cannot be written is a slow app, not a broken one.
        }
    }
}
