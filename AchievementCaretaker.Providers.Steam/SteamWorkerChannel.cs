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

namespace AchievementCaretaker.Providers.Steam
{
    /// <summary>
    /// A connection to one helper. The helper's certificate is self-made, so the usual chain
    /// check cannot vouch for it; the only certificate accepted is the one whose thumbprint
    /// the application was told, which is what makes it the helper it started.
    /// </summary>
    public sealed class SteamWorkerChannel : IDisposable
    {
        private readonly GrpcChannel _Channel;

        public SteamWorker.SteamWorkerClient Client { get; }

        /// <summary>Metadata every call must carry.</summary>
        public Metadata Headers { get; }

        public SteamWorkerChannel(SteamWorkerEndpoint endpoint)
        {
            var expected = endpoint.Thumbprint;
            var handler = new SocketsHttpHandler();
            handler.SslOptions.RemoteCertificateValidationCallback = (_, certificate, _, _) =>
                certificate != null &&
                string.Equals(new X509Certificate2(certificate).Thumbprint, expected, StringComparison.OrdinalIgnoreCase);

            this._Channel = GrpcChannel.ForAddress(
                $"https://127.0.0.1:{endpoint.Port}",
                new GrpcChannelOptions { HttpHandler = handler });
            this.Client = new SteamWorker.SteamWorkerClient(this._Channel);
            this.Headers = new Metadata { { SteamWorkerHandshake.TokenHeader, endpoint.Token } };
        }

        public void Dispose() => this._Channel.Dispose();
    }
}
