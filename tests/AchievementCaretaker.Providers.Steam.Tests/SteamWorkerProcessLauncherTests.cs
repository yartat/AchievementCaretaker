/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Xunit;

namespace AchievementCaretaker.Providers.Steam.Tests;

/// <summary>The launcher against a script that plays the helper, so no Steam and no built application is needed.</summary>
public sealed class SteamWorkerProcessLauncherTests : IDisposable
{
    private readonly string _directory = Path.Combine(Path.GetTempPath(), "sac-launch-" + Guid.NewGuid().ToString("N"));

    public SteamWorkerProcessLauncherTests() => Directory.CreateDirectory(_directory);

    public void Dispose()
    {
        try
        {
            Directory.Delete(_directory, true);
        }
        catch (IOException)
        {
        }
    }

    private string Script(string windows, string unix)
    {
        if (OperatingSystem.IsWindows())
        {
            var batch = Path.Combine(_directory, "helper.cmd");
            File.WriteAllText(batch, "@echo off\r\nset /p handshake=\r\n" + windows.Replace("\n", "\r\n"));
            return batch;
        }
        var shell = Path.Combine(_directory, "helper.sh");
        File.WriteAllText(shell, "#!/bin/sh\nread handshake\n" + unix);
        File.SetUnixFileMode(shell, UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute);
        return shell;
    }

    [Fact]
    public async Task A_helper_that_says_a_lot_afterwards_is_not_left_blocked()
    {
        var done = Path.Combine(_directory, "done.txt");
        const string line = "0123456789012345678901234567890123456789012345678901234567890123456789";
        var script = Script(
            "echo READY 1234\n" +
            $"for /L %%i in (1,1,1500) do echo {line} 1>&2\n" +
            $"for /L %%i in (1,1,1500) do echo {line}\n" +
            "echo done> \"%~dp0done.txt\"\n" +
            "more >nul\n",
            "echo 'READY 1234'\n" +
            $"i=0; while [ $i -lt 3000 ]; do echo {line} >&2; i=$((i+1)); done\n" +
            $"i=0; while [ $i -lt 3000 ]; do echo {line}; i=$((i+1)); done\n" +
            "echo done > \"$(dirname \"$0\")/done.txt\"\n" +
            "cat >/dev/null\n");

        await using var worker = await new SteamWorkerProcessLauncher(script).StartAsync(480, TestContext.Current.CancellationToken);

        for (var wait = 0; wait < 300 && File.Exists(done) == false; wait++)
        {
            await Task.Delay(100, TestContext.Current.CancellationToken);
        }
        Assert.Equal(1234, worker.Endpoint.Port);
        Assert.True(File.Exists(done), "The helper never finished writing: its output was not read.");
    }

    [Fact]
    public async Task A_helper_that_leaves_before_it_is_ready_has_its_reason_reported()
    {
        var script = Script("echo Steam is not running 1>&2\nexit /b 1\n", "echo 'Steam is not running' >&2\nexit 1\n");

        var error = await Assert.ThrowsAsync<ProviderException>(
            () => new SteamWorkerProcessLauncher(script).StartAsync(480, TestContext.Current.CancellationToken));

        Assert.Equal("worker_start", error.ErrorCode);
        Assert.Contains("Steam is not running", error.Message);
    }
}
