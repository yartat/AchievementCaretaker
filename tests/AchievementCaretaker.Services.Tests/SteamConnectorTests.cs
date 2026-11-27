/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Linq;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using AchievementCaretaker.Providers;
using AchievementCaretaker.Providers.Models;
using Xunit;

namespace AchievementCaretaker.Services.Tests;

public sealed class SteamConnectorTests
{
    private sealed class FakeEnvironment : ISteamEnvironment
    {
        public string? Unsupported { get; set; }
        public bool Installed { get; set; } = true;
        public int? ProcessId { get; set; } = 100;

        /// <summary>The threads the process list was read on.</summary>
        public ConcurrentQueue<int> ReadOn { get; } = new();

        public string? UnsupportedReason => Unsupported;
        public bool IsInstalled() => Installed;

        public int? GetRunningProcessId()
        {
            ReadOn.Enqueue(Environment.CurrentManagedThreadId);
            return ProcessId;
        }
    }

    private sealed class FakeLibrary : ISteamLibraryService
    {
        public int Asked;
        public Func<int, string> Account { get; set; } = _ => "12345";

        public Task<string> GetAccountAsync(CancellationToken cancellationToken = default)
        {
            var asked = Interlocked.Increment(ref Asked);
            return Task.FromResult(Account(asked));
        }

        public Task<IReadOnlyList<LibraryCandidate>> GetPublishedGamesAsync(CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<LibraryScan> ScanAsync(
            IReadOnlyCollection<LibraryCandidate> candidates, IProgress<LibraryScanProgress>? progress, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();

        public Task<StoreGame?> FindOwnedAsync(uint id, CancellationToken cancellationToken = default) =>
            throw new NotSupportedException();
    }

    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(10);

    private readonly FakeEnvironment _environment = new();
    private readonly FakeLibrary _library = new();

    private SteamConnector Connector() => new(_library, _environment, Tick);

    private static async Task UntilAsync(Func<bool> condition)
    {
        for (var wait = 0; wait < 500 && condition() == false; wait++)
        {
            await Task.Delay(10, TestContext.Current.CancellationToken);
        }
        Assert.True(condition(), "the condition was not reached in time");
    }

    [Fact]
    public async Task A_processor_without_a_steam_client_is_told_so_and_not_asked_again()
    {
        _environment.Unsupported = "No Steam for this processor.";
        using var connector = Connector();

        connector.Start();
        await UntilAsync(() => connector.Reason == "No Steam for this processor.");
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(connector.IsConnected);
        Assert.Equal(0, _library.Asked);
    }

    [Fact]
    public async Task Steam_that_is_not_installed_or_not_running_starts_no_helper()
    {
        _environment.Installed = false;
        using var connector = Connector();

        connector.Start();
        await UntilAsync(() => connector.Reason == "Steam is not installed.");

        _environment.Installed = true;
        _environment.ProcessId = null;
        await UntilAsync(() => connector.Reason == "Steam is not running.");

        Assert.Equal(0, _library.Asked);
        Assert.False(connector.IsConnected);
    }

    [Fact]
    public async Task A_running_steam_is_reached_through_the_helper_and_names_its_account()
    {
        using var connector = Connector();
        var changed = 0;
        connector.Changed += () => Interlocked.Increment(ref changed);

        connector.Start();
        await UntilAsync(() => Volatile.Read(ref changed) >= 1);

        Assert.Equal(12345u, connector.Account);
        Assert.Null(connector.Reason);
    }

    [Fact]
    public async Task A_steam_that_keeps_refusing_is_asked_less_and_less_often()
    {
        // The wait is counted in steps of the interval, so nothing here depends on the clock.
        var steps = 0;
        List<int> askedAt = new();
        _library.Account = _ =>
        {
            lock (askedAt)
            {
                askedAt.Add(Volatile.Read(ref steps));
            }
            throw new ProviderException("no_user", "No account is logged in.");
        };
        using SteamConnector connector = new(_library, _environment, Tick, (_, token) =>
        {
            Interlocked.Increment(ref steps);
            return Task.Delay(1, token);
        });

        connector.Start();
        await UntilAsync(() => Volatile.Read(ref _library.Asked) >= 9);

        int[] waited;
        lock (askedAt)
        {
            waited = askedAt.Skip(1).Zip(askedAt, (next, before) => next - before).Take(7).ToArray();
        }
        Assert.Equal(new[] { 1, 2, 4, 8, 16, 20, 20 }, waited);
    }

    [Fact]
    public async Task A_new_steam_process_is_asked_again_at_once()
    {
        var steps = 0;
        var restartedAt = -1;
        var reachedAt = -1;
        _library.Account = _ =>
        {
            if (Volatile.Read(ref restartedAt) < 0)
            {
                throw new ProviderException("no_user", "No account is logged in.");
            }
            reachedAt = Volatile.Read(ref steps);
            return "777";
        };
        using SteamConnector connector = new(_library, _environment, Tick, (_, token) =>
        {
            // In the middle of one of the long waits, Steam is restarted.
            if (Interlocked.Increment(ref steps) == 40)
            {
                _environment.ProcessId = 200;
                Volatile.Write(ref restartedAt, 40);
            }
            return Task.Delay(1, token);
        });

        connector.Start();
        await UntilAsync(() => connector.IsConnected);

        Assert.Equal(restartedAt, reachedAt);
    }

    /// <summary>One thread that runs what is posted to it, as a window's thread does.</summary>
    private sealed class OneThread : SynchronizationContext, IDisposable
    {
        private readonly BlockingCollection<(SendOrPostCallback Callback, object? State)> _queue = new();
        private readonly Thread _thread;

        public OneThread()
        {
            _thread = new Thread(() =>
            {
                SetSynchronizationContext(this);
                foreach (var (callback, state) in _queue.GetConsumingEnumerable())
                {
                    callback(state);
                }
            });
            _thread.Start();
        }

        public int Id => _thread.ManagedThreadId;

        public override void Post(SendOrPostCallback d, object? state)
        {
            if (_queue.IsAddingCompleted == false)
            {
                _queue.Add((d, state));
            }
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _thread.Join();
        }
    }

    [Fact]
    public async Task The_process_list_is_never_read_on_the_thread_that_runs_the_window()
    {
        _library.Account = _ => throw new ProviderException("no_user", "No account is logged in.");
        using OneThread window = new();
        using var connector = Connector();

        window.Post(_ => connector.Start(), null);
        await UntilAsync(() => Volatile.Read(ref _library.Asked) >= 3);

        Assert.DoesNotContain(window.Id, _environment.ReadOn);
    }

    [Fact]
    public async Task What_the_helper_says_is_the_reason_and_the_search_goes_on()
    {
        _library.Account = asked => asked == 1
            ? throw new ProviderException("no_user", "Steam is running, but no account is logged in.")
            : "777";
        using var connector = Connector();

        connector.Start();
        await UntilAsync(() => connector.IsConnected);

        Assert.Equal(777u, connector.Account);
        Assert.True(_library.Asked >= 2);
    }

    [Fact]
    public async Task A_steam_that_quit_is_noticed_and_the_next_one_is_a_new_connection()
    {
        using var connector = Connector();
        connector.Start();
        await UntilAsync(() => connector.IsConnected);

        _environment.ProcessId = null;
        await UntilAsync(() => connector.IsConnected == false);
        Assert.NotNull(connector.Reason);

        _library.Account = _ => "999";
        _environment.ProcessId = 200;
        await UntilAsync(() => connector.IsConnected);
        Assert.Equal(999u, connector.Account);
    }

    [Fact]
    public async Task Disposing_stops_the_search()
    {
        _environment.ProcessId = null;
        var connector = Connector();
        connector.Start();
        await UntilAsync(() => connector.Reason == "Steam is not running.");

        connector.Dispose();
        connector.Dispose();
        _environment.ProcessId = 100;
        await Task.Delay(100, TestContext.Current.CancellationToken);

        Assert.False(connector.IsConnected);
        Assert.Equal(0, _library.Asked);
    }
}
