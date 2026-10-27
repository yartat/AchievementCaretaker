/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Text;
using AchievementCaretaker.Providers.Ubisoft.Models;

namespace AchievementCaretaker.Providers.Tests;

/// <summary>
/// Builds the launcher's files, in the layout and format the Ubisoft provider reads, so a test needs no
/// Ubisoft Connect and no real file of anyone's.
/// </summary>
internal sealed class UbisoftFiles : IDisposable
{
    public const string UserId = "0a1b2c3d-4e5f-4a6b-8c7d-9e0f1a2b3c4d";

    private readonly string _root = Path.Combine(Path.GetTempPath(), "ubisoft-" + Guid.NewGuid().ToString("N"));

    public UbisoftFiles()
    {
        Locations = new UbisoftLocations(Path.Combine(_root, "launcher"), Path.Combine(_root, "archives"));
        IconDirectory = Path.Combine(_root, "icons");
        Directory.CreateDirectory(Locations.AchievementArchives);
    }

    public UbisoftLocations Locations { get; }

    public string IconDirectory { get; }

    /// <summary>Where the provider keeps archives it downloaded.</summary>
    public string DownloadDirectory => Path.Combine(_root, "downloads");

    public void Dispose()
    {
        try
        {
            Directory.Delete(_root, true);
        }
        catch (IOException)
        {
            // A temporary folder that stays is not a failed test.
        }
    }

    public static byte[] Varint(ulong value)
    {
        List<byte> bytes = new();
        while (value >= 0x80)
        {
            bytes.Add((byte)((value & 0x7F) | 0x80));
            value >>= 7;
        }
        bytes.Add((byte)value);
        return bytes.ToArray();
    }

    private static byte[] Field(int number, int wireType, byte[] payload) =>
        Varint((ulong)((number << 3) | wireType)).Concat(payload).ToArray();

    private static byte[] Message(int number, byte[] body) =>
        Field(number, 2, Varint((ulong)body.Length).Concat(body).ToArray());

    private static byte[] VarintField(int number, ulong value) => Field(number, 0, Varint(value));

    /// <summary>The ownership file: a header the provider skips, then one record per pair of ids.</summary>
    public static byte[] Ownership(params (uint First, uint? Second)[] records)
    {
        var data = new List<byte>(new byte[264]);
        foreach (var (first, second) in records)
        {
            var body = VarintField(1, first);
            if (second != null)
            {
                body = body.Concat(VarintField(2, second.Value)).ToArray();
            }
            data.AddRange(Message(1, body));
        }
        return data.ToArray();
    }

    /// <summary>A spool: one record per achievement, the id one message down and the time beside it.</summary>
    public static byte[] Spool(params (uint Id, long Time)[] records)
    {
        var data = new List<byte>();
        foreach (var (id, time) in records)
        {
            var inner = Message(1, VarintField(1, id)).Concat(VarintField(2, (ulong)time)).ToArray();
            data.AddRange(Message(1, inner));
        }
        return data.ToArray();
    }

    /// <summary>An archive as the launcher makes one: a localization per language and an icon per achievement.</summary>
    public static byte[] Archive(
        IReadOnlyDictionary<string, string> localizations,
        IReadOnlyDictionary<string, byte[]>? files = null)
    {
        using MemoryStream stream = new();
        using (ZipArchive zip = new(stream, ZipArchiveMode.Create, true))
        {
            foreach (var (locale, text) in localizations)
            {
                using var writer = new StreamWriter(zip.CreateEntry(locale + "_loc.txt").Open(), new UTF8Encoding(false));
                writer.Write(text);
            }

            foreach (var (name, content) in files ?? new Dictionary<string, byte[]>())
            {
                using var entry = zip.CreateEntry(name).Open();
                entry.Write(content);
            }
        }
        return stream.ToArray();
    }

    /// <summary>Puts the ownership file where the launcher keeps it, named by the user's id.</summary>
    public string WriteOwnership(byte[] data, string name = UserId)
    {
        Directory.CreateDirectory(Locations.OwnershipDirectory);
        var path = Path.Combine(Locations.OwnershipDirectory, name);
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>The configuration catalog: one record per product, with its ids and its block of YAML.</summary>
    public static byte[] Configurations(params (uint ProductId, uint? LaunchId, string Yaml)[] records)
    {
        var bytes = new List<byte>();
        foreach (var (productId, launchId, yaml) in records)
        {
            var body = VarintField(1, productId).AsEnumerable();
            if (launchId != null)
            {
                body = body.Concat(VarintField(2, launchId.Value));
            }
            var text = System.Text.Encoding.UTF8.GetBytes(yaml);
            body = body.Concat(Field(3, 2, Varint((ulong)text.Length).Concat(text).ToArray()));
            bytes.AddRange(Message(1, body.ToArray()));
        }
        return bytes.ToArray();
    }

    /// <summary>Puts the catalog where the launcher keeps it.</summary>
    public string WriteConfigurations(byte[] data)
    {
        Directory.CreateDirectory(Path.GetDirectoryName(Locations.ConfigurationFile)!);
        File.WriteAllBytes(Locations.ConfigurationFile, data);
        return Locations.ConfigurationFile;
    }

    /// <summary>Puts a game's spool where the launcher keeps it.</summary>
    public string WriteSpool(uint gameId, byte[] data, string userId = UserId)
    {
        var directory = Path.Combine(Locations.SpoolDirectory, userId);
        Directory.CreateDirectory(directory);
        var path = Path.Combine(directory, gameId + ".spool");
        File.WriteAllBytes(path, data);
        return path;
    }

    /// <summary>Puts a game's archive in the launcher's folder, under the name it would have.</summary>
    public string WriteArchive(uint gameId, byte[] data, string name = "")
    {
        var path = Path.Combine(
            Locations.AchievementArchives,
            name.Length > 0 ? name : gameId + "_5f2c9b1e7a3d4c8e9b0a1c2d3e4f5a6b.zip");
        File.WriteAllBytes(path, data);
        return path;
    }
}
