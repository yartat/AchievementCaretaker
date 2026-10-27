/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Net.Http;
using System.Security.Cryptography.X509Certificates;
using AchievementCaretaker.Providers.Steam.Protocol;
using Grpc.Core;
using Grpc.Net.Client;
using AchievementCaretaker.Providers.Steam.Models;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>
/// A connection to one helper. The helper's certificate is self-made, so the usual chain
/// check cannot vouch for it; the only certificate accepted is the one whose thumbprint
/// the application was told, which is what makes it the helper it started.
/// </summary>
public sealed class SteamWorkerChannel : IDisposable
{
    private readonly GrpcChannel _channel;

    /// <summary>The gRPC client for the helper's service.</summary>
    public SteamWorker.SteamWorkerClient Client { get; }

    /// <summary>Metadata every call must carry.</summary>
    public Metadata Headers { get; }

    /// <summary>Connects to a helper, trusting only the certificate the endpoint names.</summary>
    /// <param name="endpoint">Where the helper listens, with its certificate's thumbprint and the token.</param>
    public SteamWorkerChannel(SteamWorkerEndpoint endpoint)
    {
        var expected = endpoint.Thumbprint;
        var handler = new SocketsHttpHandler();
        handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
            certificate != null &&
            string.Equals(new X509Certificate2(certificate).Thumbprint, expected, StringComparison.OrdinalIgnoreCase);

        _channel = GrpcChannel.ForAddress(
            $"https://127.0.0.1:{endpoint.Port}",
            new GrpcChannelOptions { HttpHandler = handler });
        Client = new SteamWorker.SteamWorkerClient(_channel);
        Headers = new Metadata { { SteamWorkerHandshake.TokenHeader, endpoint.Token } };
    }

    /// <summary>Closes the connection.</summary>
    public void Dispose() => _channel.Dispose();
}
