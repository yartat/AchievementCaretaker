/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Diagnostics;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;

namespace AchievementCaretaker.Providers.Steam
{
    /// <summary>
    /// Starts the helper as a hidden copy of the application's own executable with
    /// <c>--steam-worker &lt;appid&gt;</c>, hands it its certificate and token on standard input
    /// and waits for it to say which port it is listening on.
    /// </summary>
    public sealed class SteamWorkerProcessLauncher : ISteamWorkerLauncher
    {
        /// <summary>The command-line switch that makes the executable run as a helper.</summary>
        public const string WorkerSwitch = "--steam-worker";

        private static readonly TimeSpan StartTimeout = TimeSpan.FromSeconds(30);

        private readonly string _Executable;

        /// <param name="executable">The application's executable, which is what the helper is a copy of.</param>
        public SteamWorkerProcessLauncher(string executable)
        {
            this._Executable = executable;
        }

        public async Task<ISteamWorker> StartAsync(long appId, CancellationToken cancellationToken)
        {
            var handshake = SteamWorkerHandshake.Create();
            ProcessStartInfo info = new(this._Executable)
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardInput = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
            };
            info.ArgumentList.Add(WorkerSwitch);
            info.ArgumentList.Add(appId.ToString(CultureInfo.InvariantCulture));

            var process = Process.Start(info) ?? throw new ProviderException("worker_start", "The Steam helper did not start.");
            SteamWorkerProcess worker = new(process, new SteamWorkerEndpoint(0, handshake.Thumbprint, handshake.Token));
            try
            {
                await process.StandardInput.WriteLineAsync(handshake.ToLine().AsMemory(), cancellationToken);
                await process.StandardInput.FlushAsync(cancellationToken);

                using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeout.CancelAfter(StartTimeout);
                var line = await process.StandardOutput.ReadLineAsync(timeout.Token);
                if (line == null || line.StartsWith("READY ", StringComparison.Ordinal) == false ||
                    int.TryParse(line.AsSpan(6), NumberStyles.None, CultureInfo.InvariantCulture, out var port) == false)
                {
                    throw new ProviderException("worker_start", await ReasonAsync(process));
                }
                return worker.WithPort(port);
            }
            catch
            {
                await worker.DisposeAsync();
                throw;
            }
        }

        /// <summary>What the helper said before it left, or a plain statement that it never got going.</summary>
        private static async Task<string> ReasonAsync(Process process)
        {
            try
            {
                using CancellationTokenSource wait = new(TimeSpan.FromSeconds(3));
                await process.WaitForExitAsync(wait.Token);
                var said = (await process.StandardError.ReadToEndAsync(wait.Token)).Trim();
                if (string.IsNullOrEmpty(said) == false)
                {
                    return said;
                }
            }
            catch (Exception)
            {
                // Still running, or already gone: fall through to the plain statement.
            }
            return "The Steam helper did not say it was ready.";
        }

        private sealed class SteamWorkerProcess : ISteamWorker
        {
            private readonly Process _Process;

            public SteamWorkerEndpoint Endpoint { get; private set; }

            public SteamWorkerProcess(Process process, SteamWorkerEndpoint endpoint)
            {
                this._Process = process;
                this.Endpoint = endpoint;
            }

            public SteamWorkerProcess WithPort(int port)
            {
                this.Endpoint = this.Endpoint with { Port = port };
                return this;
            }

            public async ValueTask DisposeAsync()
            {
                try
                {
                    // Closing standard input is the helper's cue to exit; it also covers this
                    // process dying, which is why it is the signal and not a call.
                    this._Process.StandardInput.Close();
                    using CancellationTokenSource grace = new(TimeSpan.FromSeconds(5));
                    await this._Process.WaitForExitAsync(grace.Token);
                }
                catch (Exception)
                {
                    try
                    {
                        this._Process.Kill(true);
                    }
                    catch (Exception)
                    {
                    }
                }
                finally
                {
                    this._Process.Dispose();
                }
            }
        }
    }
}
