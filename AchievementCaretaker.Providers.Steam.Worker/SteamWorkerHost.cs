/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Security.Authentication;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AchievementCaretaker.Providers.Steam;

/// <summary>
/// The helper's server: gRPC over TLS on a free port of 127.0.0.1, serving one
/// <see cref="ISteamAppBackend"/> to callers that present the token.
/// </summary>
public sealed class SteamWorkerHost : IAsyncDisposable
{
    private readonly WebApplication _app;
    private readonly TaskCompletionSource _exitRequested;

    /// <summary>The port it is listening on.</summary>
    public int Port { get; }

    /// <summary>Completes when a caller has asked the helper to exit.</summary>
    public Task ExitRequested => _exitRequested.Task;

    private SteamWorkerHost(WebApplication app, int port, TaskCompletionSource exitRequested)
    {
        _app = app;
        Port = port;
        _exitRequested = exitRequested;
    }

    /// <summary>Starts serving the backend over TLS on a port of the loopback address that the system picks.</summary>
    /// <param name="backend">What the service does with Steam.</param>
    /// <param name="handshake">The certificate to serve with and the token every call must carry.</param>
    public static async Task<SteamWorkerHost> StartAsync(ISteamAppBackend backend, SteamWorkerHandshake handshake)
    {
        TaskCompletionSource exit = new(TaskCreationOptions.RunContinuationsAsynchronously);
        var certificate = handshake.LoadCertificate();

        var builder = WebApplication.CreateSlimBuilder();
        builder.Logging.ClearProviders();
        builder.WebHost.ConfigureKestrel(kestrel =>
        {
            kestrel.Listen(IPAddress.Loopback, 0, listen =>
            {
                listen.Protocols = HttpProtocols.Http2;
                listen.UseHttps(https =>
                {
                    https.ServerCertificate = certificate;
                    https.SslProtocols = SslProtocols.Tls12 | SslProtocols.Tls13;
                });
            });
        });
        builder.Services.AddGrpc(options =>
        {
            options.Interceptors.Add<TokenInterceptor>();
            // Local, and the caller is the application: the reason is more use than a bare "Internal".
            options.EnableDetailedErrors = true;
        });
        builder.Services.AddSingleton(new TokenInterceptor(handshake.Token));
        builder.Services.AddSingleton(_ => new SteamWorkerService(backend, () => exit.TrySetResult()));

        var app = builder.Build();
        app.MapGrpcService<SteamWorkerService>();
        await app.StartAsync();

        var addresses = app.Services.GetRequiredService<Microsoft.AspNetCore.Hosting.Server.IServer>()
            .Features.Get<Microsoft.AspNetCore.Hosting.Server.Features.IServerAddressesFeature>()!.Addresses;
        var address = new Uri(addresses.First());
        return new SteamWorkerHost(app, address.Port, exit);
    }

    /// <summary>
    /// The helper's whole life when started by the application: read the handshake from
    /// standard input, serve, say which port, and exit when asked to or when standard input
    /// closes (the application is gone or is done with it).
    /// </summary>
    public static async Task RunAsync(ISteamAppBackend backend, TextReader input, TextWriter output)
    {
        var line = await input.ReadLineAsync();
        if (line == null)
        {
            // Started and abandoned before it was told anything.
            return;
        }
        var handshake = SteamWorkerHandshake.FromLine(line);

        await using var host = await StartAsync(backend, handshake);
        await output.WriteLineAsync($"READY {host.Port}");
        await output.FlushAsync();

        // Standard input ending is the other way out.
        var closed = Task.Run(async () =>
        {
            while (await input.ReadLineAsync() != null)
            {
            }
        });
        await Task.WhenAny(host.ExitRequested, closed);
    }

    /// <summary>Stops serving and releases the port.</summary>
    public async ValueTask DisposeAsync()
    {
        await _app.StopAsync();
        await _app.DisposeAsync();
    }
}
