/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Globalization;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;

namespace AchievementCaretaker.Services;

/// <summary>
/// Looks for Steam in the background and says when it is there, and who is logged in.
/// </summary>
/// <remarks>
/// <para>
/// The library window has other sources than Steam, so it must open without it. This keeps
/// asking, off the UI thread, every few seconds. The application holds no connection to Steam of
/// its own: "Steam is there" means a hidden helper was started, reached Steam and named the
/// account (<see cref="ISteamLibraryService.GetAccountAsync"/>), and the answer to why not is that
/// helper's own message. The machine is asked first (<see cref="ISteamEnvironment"/>), so no helper
/// is started for a Steam that is not installed or not running.
/// </para>
/// <para>
/// <see cref="Start"/> has to be called on the UI thread: the loop resumes on the thread that
/// started it, which is what makes <see cref="Changed"/> arrive there and lets a subscriber touch
/// its view model without marshalling.
/// </para>
/// <para>
/// It goes on watching after it connects. When that Steam goes, <see cref="Reason"/> says so,
/// <see cref="Changed"/> is raised and the search starts again; a Steam that comes back is a new
/// connection.
/// </para>
/// </remarks>
public sealed class SteamConnector : IDisposable
{
    /// <summary>How long to wait between tries when nothing else is asked for.</summary>
    public static readonly TimeSpan DefaultInterval = TimeSpan.FromSeconds(3);

    private readonly ISteamLibraryService _library;
    private readonly ISteamEnvironment _environment;
    private readonly TimeSpan _interval;
    private readonly CancellationTokenSource _stop = new();

    /// <summary>The 32-bit account id Steam is logged in as, or null until Steam has been reached.</summary>
    public uint? Account { get; private set; }

    /// <summary>Whether Steam has been reached and is still the one that was.</summary>
    public bool IsConnected => Account != null;

    /// <summary>Why Steam is not there, or null once it is.</summary>
    public string? Reason { get; private set; } = "Looking for Steam...";

    /// <summary>Raised on the UI thread when <see cref="Reason"/> changes or Steam is reached or lost.</summary>
    public event Action? Changed;

    /// <summary>Makes the connector; nothing is asked until <see cref="Start"/>.</summary>
    /// <param name="library">Asked who is logged in, which is how Steam being there is learned.</param>
    /// <param name="environment">Asked first whether Steam is installed and running.</param>
    /// <param name="interval">How long to wait between tries. Only a test has a reason to pass one.</param>
    public SteamConnector(ISteamLibraryService library, ISteamEnvironment environment, TimeSpan? interval = null)
    {
        _library = library;
        _environment = environment;
        _interval = interval ?? DefaultInterval;
    }

    /// <summary>Starts looking for Steam. Call it on the UI thread: see the class remarks.</summary>
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

                Account = found.Value.Account;
                Reason = null;
                Changed?.Invoke();

                await SteamWatch.WaitUntilGoneAsync(found.Value.ProcessId, _environment.GetRunningProcessId, _interval, token);

                Account = null;
                Reason = "Steam has stopped.";
                Changed?.Invoke();
            }
        }
        catch (OperationCanceledException)
        {
            // Disposed.
        }
    }

    /// <summary>Tries until Steam is reached. Null when it will not be: disposed, or a failure that cannot be retried.</summary>
    private async Task<(uint Account, int? ProcessId)?> FindAsync(CancellationToken token)
    {
        while (true)
        {
            var (found, reason, canRetry) = await TryAsync(token);
            if (found != null)
            {
                return found;
            }

            if (reason != Reason)
            {
                Reason = reason;
                Changed?.Invoke();
            }
            if (canRetry == false)
            {
                return null;
            }

            await Task.Delay(_interval, token);
        }
    }

    private async Task<((uint Account, int? ProcessId)? Found, string? Reason, bool CanRetry)> TryAsync(CancellationToken token)
    {
        var unsupported = _environment.UnsupportedReason;
        if (unsupported != null)
        {
            return (null, unsupported, false);
        }

        // Off the UI thread: the registry and the process list.
        var (installed, processId) = await Task.Run(() => (_environment.IsInstalled(), _environment.GetRunningProcessId()), token);
        if (installed == false)
        {
            return (null, "Steam is not installed.", true);
        }
        if (processId == null)
        {
            return (null, "Steam is not running.", true);
        }

        try
        {
            var account = await _library.GetAccountAsync(token);
            return ((uint.Parse(account, CultureInfo.InvariantCulture), processId), null, true);
        }
        catch (ProviderException e)
        {
            return (null, e.Message, true);
        }
    }

    /// <summary>Stops looking.</summary>
    public void Dispose()
    {
        _stop.Cancel();

    }
}
