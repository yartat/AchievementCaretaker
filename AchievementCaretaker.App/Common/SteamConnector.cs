/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Threading;
using System.Threading.Tasks;

namespace AchievementCaretaker.App.Common;

/// <summary>
/// What one try at reaching Steam came to.
/// </summary>
/// <param name="Client">The connected client, or null.</param>
/// <param name="Reason">Why there is none, in words for the user.</param>
/// <param name="CanRetry">False when trying again cannot help, as on a CPU Valve ships no client for.</param>
/// <param name="ProcessId">The Steam client's process id when connected, for noticing it go. Null when it cannot be told.</param>
internal readonly record struct SteamConnectAttempt(
    SteamApi.Client Client,
    string Reason,
    bool CanRetry,
    int? ProcessId = null);

/// <summary>
/// Looks for Steam in the background and hands over a client once there is one.
/// </summary>
/// <remarks>
/// <para>
/// The library window has other sources than Steam, so it must open without it. This
/// keeps trying to create the Steam pipe, off the UI thread, every few seconds, and
/// only when that works does it say so; the window starts refreshing the Steam
/// library then and not before. Until then <see cref="Reason"/> says what is missing.
/// </para>
/// <para>
/// <see cref="Start"/> has to be called on the UI thread: the loop resumes on the
/// thread that started it, which is what makes <see cref="Changed"/> arrive there and
/// lets a subscriber touch its view model without marshalling. Each try is a fresh
/// <see cref="SteamApi.Client"/>; the one that failed is disposed.
/// </para>
/// <para>
/// It goes on watching after it connects. When that Steam goes, the client is
/// abandoned (its pipe is dead, so nothing is released), <see cref="Reason"/> says so,
/// <see cref="Changed"/> is raised and the search starts again; a Steam that comes
/// back is a new connection and a new client. Whether the already-loaded client
/// library can open a pipe to a Steam that started after it was loaded is Steam's to
/// say, and was not exercised against a real restart.
/// </para>
/// </remarks>
internal sealed class SteamConnector : IDisposable
{
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(3);

    private readonly Func<SteamConnectAttempt> _attempt;
    private readonly Func<int?> _probe;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>Null until Steam has been reached.</summary>
    public SteamApi.Client Client { get; private set; }

    private int? _connectedProcessId;

    /// <summary>
    /// Whether the Steam this was connected to is still the one running, asked directly
    /// and now. The watch notices a loss only every few seconds, which is long enough
    /// for a whole job to run against a dead pipe and call its empty answers the truth;
    /// a job that must not do that asks this when it finishes.
    /// </summary>
    /// <remarks>
    /// Safe off the UI thread. With no process id on record there is no way to tell,
    /// and a connection that exists is taken to be held.
    /// </remarks>
    public bool IsSteamStillRunning()
    {
        if (Client == null)
        {
            return false;
        }

        var recorded = _connectedProcessId;
        if (recorded == null)
        {
            return true;
        }

        var probe = _probe ?? SteamApi.SteamPlatform.GetRunningClientProcessId;
        return probe() == recorded;
    }

    public bool IsConnected => Client != null;

    /// <summary>Why Steam is not there, or null once it is.</summary>
    public string Reason { get; private set; } = "Looking for Steam...";

    /// <summary>Raised on the UI thread when <see cref="Reason"/> changes or Steam is reached.</summary>
    public event Action Changed;

    /// <param name="attempt">What one try is. Only a test has a reason to pass one.</param>
    /// <param name="probe">What tells which Steam is running. Likewise.</param>
    public SteamConnector(
        Func<SteamConnectAttempt> attempt = null,
        TimeSpan? interval = null,
        Func<int?> probe = null)
    {
        _attempt = attempt ?? TryConnect;
        _probe = probe;
        _interval = interval ?? DefaultInterval;
    }

    public void Start()
    {
        // The loop reports its own end; nothing waits on it.
        Task loop = RunAsync();
        GC.KeepAlive(loop);
    }

    private async Task RunAsync()
    {
        var token = _stop.Token;
        try
        {
            while (token.IsCancellationRequested == false)
            {
                var found = await FindAsync(token);
                if (found == null)
                {
                    return;
                }

                _connectedProcessId = found.Value.ProcessId;
                Client = found.Value.Client;
                Reason = null;
                Changed?.Invoke();

                await SteamWatch.WaitUntilGoneAsync(found.Value.ProcessId, token, _probe, _interval);

                // Steam is gone. Its pipe is dead, so the client is dropped without
                // being released, and the search starts over.
                var gone = Client;
                Client = null;
                _connectedProcessId = null;
                Reason = "Steam has stopped.";
                gone?.Abandon();
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }

    /// <summary>
    /// Tries until Steam is reached. Null when it will not be: disposed, or a
    /// failure that cannot be retried.
    /// </summary>
    private async Task<SteamConnectAttempt?> FindAsync(CancellationToken token)
    {
        while (true)
        {
            var result = await Task.Run(_attempt, token);

            // Disposed while the try was in flight: it may have succeeded, and
            // nothing is left to take the client.
            if (token.IsCancellationRequested == true)
            {
                result.Client?.Dispose();
                return null;
            }

            if (result.Client != null)
            {
                return result;
            }

            if (result.Reason != Reason)
            {
                Reason = result.Reason;
                Changed?.Invoke();
            }
            if (result.CanRetry == false)
            {
                return null;
            }

            await Task.Delay(_interval, token);
        }
    }

    /// <summary>
    /// Opens a session for the library: application id 0, which is not a game but
    /// how Steam is asked for one covering the whole account.
    /// </summary>
    private static SteamConnectAttempt TryConnect()
    {
        if (SteamApi.SteamPlatform.IsArchitectureSupported == false)
        {
            return new(null, SteamApi.SteamPlatform.DescribeUnsupportedArchitecture(), false);
        }

        SteamApi.Client client = new();
        try
        {
            client.Initialize(0);
            return new(client, null, true, SteamApi.SteamPlatform.GetRunningClientProcessId());
        }
        catch (SteamApi.ClientInitializeException e)
        {
            client.Dispose();
            return new(null, Describe(e.Failure), true);
        }
        catch (DllNotFoundException)
        {
            client.Dispose();
            return new(null, "The Steam client library could not be loaded.", true);
        }
    }

    private static string Describe(SteamApi.ClientInitializeFailure failure)
    {
        return failure switch
        {
            SteamApi.ClientInitializeFailure.GetInstallPath => "Steam is not installed.",
            SteamApi.ClientInitializeFailure.Load => "The Steam client library could not be loaded.",
            SteamApi.ClientInitializeFailure.CreateSteamClient => "The Steam client library could not be loaded.",
            SteamApi.ClientInitializeFailure.CreateSteamPipe => "Steam is not running.",
            SteamApi.ClientInitializeFailure.ConnectToGlobalUser => "Steam is running, but no account is logged in.",
            _ => "Could not connect to Steam.",
        };
    }

    public void Dispose()
    {
        _stop.Cancel();
        Client?.Dispose();
        Client = null;
    }
}
