/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * This file is based on UplayKit by UplayDB (MIT)
 * https://github.com/UplayDB/UplayKit
 * See docs/ATTRIBUTION.md.
 *
 * See LICENSE in the repository root.
 */

using System.IO;
using System.Net.Security;
using System.Net.Sockets;
using System.Threading;
using System.Threading.Tasks;

namespace AchievementCaretaker.Providers.Ubisoft;

/// <summary>Makes the connection to Ubisoft's socket service. An interface so that a test can stand in for the server.</summary>
public interface IUbisoftConnector
{
    /// <summary>Connects and returns the stream the service's messages travel on, TLS already done.</summary>
    /// <param name="cancellationToken">The token to give up.</param>
    Task<Stream> ConnectAsync(CancellationToken cancellationToken);
}

/// <summary>The real connector: TLS to Ubisoft's Demux host.</summary>
public sealed class UbisoftConnector : IUbisoftConnector
{
    private const string Host = "dmx.upc.ubisoft.com";
    private const int Port = 443;

    /// <inheritdoc />
    public async Task<Stream> ConnectAsync(CancellationToken cancellationToken)
    {
        TcpClient tcp = new() { NoDelay = true };
        try
        {
            await tcp.ConnectAsync(Host, Port, cancellationToken).ConfigureAwait(false);
            SslStream tls = new(tcp.GetStream(), leaveInnerStreamOpen: false);
            await tls.AuthenticateAsClientAsync(new SslClientAuthenticationOptions { TargetHost = Host }, cancellationToken).ConfigureAwait(false);
            return tls;
        }
        catch
        {
            tcp.Dispose();
            throw;
        }
    }
}
