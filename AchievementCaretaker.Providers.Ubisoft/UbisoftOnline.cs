/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on UplayKit by UplayDB (MIT)
 * https://github.com/UplayDB/UplayKit
 * and on Uplay-Protobufs by UplayDB (MIT)
 * https://github.com/UplayDB/Uplay-Protobufs
 * It makes the same calls their client does: the version check, the sign-in with a ticket, the ownership
 * service and the achievements service. See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers.Ubisoft.Models;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>A product from the account's ownership list, as Ubisoft's server sends it.</summary>
/// <param name="ProductId">The product's id.</param>
/// <param name="Owned">The account owns it (an entry can also be a product that is only visible).</param>
/// <param name="ProductType">0 is a game; add-ons, season passes and bundles are other numbers.</param>
/// <param name="State">3 is playable.</param>
/// <param name="LockedBySubscription">It is owned through a subscription that has lapsed.</param>
/// <param name="Configuration">The same block of YAML the launcher keeps in its catalog, or null when the server left it out.</param>
internal sealed record OnlineProduct(uint ProductId, bool Owned, uint ProductType, uint State, bool LockedBySubscription, string? Configuration);

/// <summary>What the server has recorded as earned for one achievement.</summary>
/// <param name="Id">The achievement's id.</param>
/// <param name="EarnedAt">When, or null when the record has no usable time.</param>
internal sealed record OnlineAchievement(uint Id, DateTimeOffset? EarnedAt);

/// <summary>
/// An account's session on Ubisoft's socket service: signed in with its ticket, with the ownership and
/// achievement services opened on demand. One per connection; it is dropped when the connection is.
/// </summary>
internal sealed class UbisoftOnline : IAsyncDisposable
{
    /// <summary>The protocol version to announce when the server will not say which is current.</summary>
    private const uint FallbackVersion = 12922;

    private const string Platform = "PC";

    private readonly DemuxClient _client;
    private readonly UbisoftSession _session;
    private uint? _ownership;
    private uint? _achievements;
    private uint _serviceRequest;

    private UbisoftOnline(DemuxClient client, UbisoftSession session)
    {
        _client = client;
        _session = session;
    }

    /// <summary>False once the connection has dropped.</summary>
    public bool IsAlive => _client.IsAlive;

    /// <summary>
    /// Connects, announces the protocol version the server says is current, and signs in with the session's
    /// ticket.
    /// </summary>
    /// <param name="connector">Where to connect.</param>
    /// <param name="session">The session; it needs a valid ticket and session id.</param>
    /// <param name="cancellationToken">The token to give up.</param>
    /// <exception cref="DemuxException">The connection failed.</exception>
    /// <exception cref="ProviderException">The server refused the ticket (<c>login_required</c>).</exception>
    public static async Task<UbisoftOnline> ConnectAsync(IUbisoftConnector connector, UbisoftSession session, CancellationToken cancellationToken)
    {
        var stream = await connector.ConnectAsync(cancellationToken).ConfigureAwait(false);
        var client = DemuxClient.Start(stream);
        try
        {
            var patch = await client.RequestAsync(
                id => new ProtoMessage().Varint(1, id).Message(5, new ProtoMessage().Text(1, "DEFAULT").Varint(2, 0)),
                cancellationToken).ConfigureAwait(false);
            var latest = (uint)DemuxClient.Field(ProtoMessage.Read(DemuxClient.Field(patch, 5).Bytes ?? Array.Empty<byte>()), 5).Varint;
            await client.PushVersionAsync(latest == 0 ? FallbackVersion : latest, cancellationToken).ConfigureAwait(false);

            var token = new ProtoMessage().Text(3, session.Ticket ?? "");
            var authenticated = await client.RequestAsync(
                id => new ProtoMessage().Varint(1, id).Message(
                    2,
                    new ProtoMessage().Message(1, token).Varint(4, 0).Text(2, "uplay_pc")),
                cancellationToken).ConfigureAwait(false);
            var result = ProtoMessage.Read(DemuxClient.Field(authenticated, 2).Bytes ?? Array.Empty<byte>());
            if (DemuxClient.Field(result, 1).Varint == 0)
            {
                throw new ProviderException("login_required", "Ubisoft did not accept the saved sign-in. Sign in again.");
            }
        }
        catch
        {
            await client.DisposeAsync().ConfigureAwait(false);
            throw;
        }

        return new UbisoftOnline(client, session);
    }

    /// <summary>Everything the account owns that is a game it can play.</summary>
    /// <param name="cancellationToken">The token to give up.</param>
    public async Task<IReadOnlyList<OnlineProduct>> GetProductsAsync(CancellationToken cancellationToken)
    {
        var reply = await OwnershipAsync(
            request => request.Message(2, new ProtoMessage().Varint(2, 1).Varint(3, 7).Varint(5, 0)),
            cancellationToken).ConfigureAwait(false);

        var initialize = ProtoMessage.Read(DemuxClient.Field(reply, 2).Bytes ?? Array.Empty<byte>());
        if (DemuxClient.Field(initialize, 1).Varint == 0)
        {
            throw new DemuxException("Ubisoft would not list the account's games.");
        }

        List<OnlineProduct> products = new();
        var owned = ProtoMessage.Read(DemuxClient.Field(initialize, 2).Bytes ?? Array.Empty<byte>());
        foreach (var game in owned.Where(field => field.Number == 1 && field.Bytes != null))
        {
            var fields = ProtoMessage.Read(game.Bytes!);
            var id = DemuxClient.Field(fields, 1).Varint;
            if (id is 0 or > uint.MaxValue)
            {
                continue;
            }

            var configuration = DemuxClient.Field(fields, 17);
            products.Add(new OnlineProduct(
                (uint)id,
                DemuxClient.Field(fields, 20).Varint != 0,
                (uint)DemuxClient.Field(fields, 8).Varint,
                (uint)DemuxClient.Field(fields, 13).Varint,
                DemuxClient.Field(fields, 37).Varint != 0,
                configuration.Bytes is { Length: > 0 } ? configuration.Text : null));
        }
        return products;
    }

    /// <summary>The catalog block of one product, for a product whose block the list left out; null when the server has none.</summary>
    /// <param name="productId">The product.</param>
    /// <param name="cancellationToken">The token to give up.</param>
    public async Task<string?> GetConfigurationAsync(uint productId, CancellationToken cancellationToken)
    {
        var reply = await OwnershipAsync(
            request => request.Message(9, new ProtoMessage().Varint(1, productId)),
            cancellationToken).ConfigureAwait(false);

        var configuration = ProtoMessage.Read(DemuxClient.Field(reply, 9).Bytes ?? Array.Empty<byte>());
        return DemuxClient.Field(configuration, 1).Varint == 1 && DemuxClient.Field(configuration, 3).Bytes is { Length: > 0 } text
            ? System.Text.Encoding.UTF8.GetString(text)
            : null;
    }

    /// <summary>What the server has recorded as earned for a game.</summary>
    /// <param name="syncId">
    /// The key the achievements service knows the game by: the catalog block's <c>achievements_sync_id</c>, not
    /// the product id (asking by product id answers with an empty list).
    /// </param>
    /// <param name="cancellationToken">The token to give up.</param>
    public async Task<IReadOnlyList<OnlineAchievement>> GetAchievementsAsync(string syncId, CancellationToken cancellationToken)
    {
        _achievements ??= await _client.OpenServiceAsync("ach_frontend", cancellationToken).ConfigureAwait(false);

        var product = new ProtoMessage().Text(1, syncId).Text(2, Platform);
        var read = new ProtoMessage().Text(1, _session.UserId ?? "").Message(2, product);
        var request = new ProtoMessage().Varint(1, ++_serviceRequest).Message(3, read);

        var reply = ProtoMessage.Read(await _client.CallAsync(_achievements.Value, request.ToArray(), cancellationToken).ConfigureAwait(false));
        var blob = ProtoMessage.Read(DemuxClient.Field(ProtoMessage.Read(DemuxClient.Field(reply, 3).Bytes ?? Array.Empty<byte>()), 2).Bytes ?? Array.Empty<byte>());

        List<OnlineAchievement> earned = new();
        foreach (var forProduct in blob.Where(field => field.Number == 1 && field.Bytes != null))
        {
            var list = ProtoMessage.Read(DemuxClient.Field(ProtoMessage.Read(forProduct.Bytes!), 2).Bytes ?? Array.Empty<byte>());
            foreach (var entry in list.Where(field => field.Number == 1 && field.Bytes != null))
            {
                var fields = ProtoMessage.Read(entry.Bytes!);
                var id = DemuxClient.Field(fields, 1).Varint;
                var time = DemuxClient.Field(fields, 2).Varint;
                if (id is 0 or > uint.MaxValue)
                {
                    continue;
                }

                DateTimeOffset? when = time is > 0 and <= long.MaxValue && SpoolFile.TryToDate((long)time, out var date) == true ? date : null;
                earned.Add(new OnlineAchievement((uint)id, when));
            }
        }
        return earned;
    }

    /// <inheritdoc />
    public ValueTask DisposeAsync() => _client.DisposeAsync();

    /// <summary>One call to the ownership service: the <c>Req</c> with its ids and the session's ticket, in Upstream, and the <c>Rsp</c> back.</summary>
    private async Task<List<ProtoField>> OwnershipAsync(Func<ProtoMessage, ProtoMessage> fill, CancellationToken cancellationToken)
    {
        _ownership ??= await _client.OpenServiceAsync("ownership_service", cancellationToken).ConfigureAwait(false);

        var request = new ProtoMessage().Varint(1, ++_serviceRequest);
        fill(request);
        request.Text(30, _session.Ticket ?? "").Text(31, _session.SessionId ?? "");

        var upstream = new ProtoMessage().Message(1, request);
        var reply = ProtoMessage.Read(await _client.CallAsync(_ownership.Value, upstream.ToArray(), cancellationToken).ConfigureAwait(false));
        return ProtoMessage.Read(DemuxClient.Field(reply, 1).Bytes ?? Array.Empty<byte>());
    }
}
