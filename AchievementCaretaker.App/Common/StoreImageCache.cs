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

    /// <summary>Returns null when the image is not cached.</summary>
    public byte[] TryRead(string provider, string url)
    {
        if (string.IsNullOrEmpty(url) == true)
        {
            return null;
        }
        try
        {
            var file = FileFor(provider, url);
            return File.Exists(file) == true ? File.ReadAllBytes(file) : null;
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

        var cached = await Task.Run(() => Decode(TryRead(provider, url), decodeWidth), cancellationToken);
        if (cached != null)
        {
            return cached;
        }

        try
        {
            var data = await _http.CreateClient().GetByteArrayAsync(new Uri(url), cancellationToken);
            var bitmap = await Task.Run(() => Decode(data, decodeWidth), cancellationToken);
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

    /// <summary>Null for no data, and for data that is not an image: a truncated file means a re-download.</summary>
    private static Bitmap Decode(byte[] data, int? decodeWidth)
    {
        if (data == null)
        {
            return null;
        }
        try
        {
            using MemoryStream stream = new(data, false);
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
