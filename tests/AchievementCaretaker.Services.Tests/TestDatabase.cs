/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.IO;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AchievementCaretaker.Services.Tests;

/// <summary>
/// The library database as the application registers it, in a temporary folder: the real
/// registration, the real migration, and one scope, as one window has.
/// </summary>
internal sealed class TestDatabase : IDisposable
{
    private readonly ServiceProvider _root;
    private IServiceScope _scope;

    public string Directory { get; } = Path.Combine(Path.GetTempPath(), "sac-test-" + Guid.NewGuid().ToString("N"));

    public string DatabasePath { get; }

    public DatabaseState State => _root.GetRequiredService<DatabaseState>();

    public IServiceProvider Root => _root;

    /// <param name="databasePath">Where the database goes; the default is a fresh file in the temporary folder.</param>
    /// <param name="migrate">Whether to prepare the database, as the application does at start.</param>
    public TestDatabase(string? databasePath = null, bool migrate = true)
    {
        System.IO.Directory.CreateDirectory(Directory);
        DatabasePath = databasePath ?? Path.Combine(Directory, "games.db");

        _root = new ServiceCollection()
            .AddLibraryDatabase(DatabasePath)
            .BuildServiceProvider(new ServiceProviderOptions { ValidateScopes = true, ValidateOnBuild = true });
        if (migrate == true)
        {
            _root.MigrateLibraryDatabase();
        }
        _scope = _root.CreateScope();
    }

    public IServiceProvider Services => _scope.ServiceProvider;

    public IGameRepository Repository => Services.GetRequiredService<IGameRepository>();

    /// <summary>A new scope, as a second window or the next start would have.</summary>
    public IServiceProvider NewScope() => _root.CreateScope().ServiceProvider;

    /// <summary>Ends the scope, which closes its connection; a new one is made on the next call.</summary>
    public void Reopen()
    {
        _scope.Dispose();
        _scope = _root.CreateScope();
    }

    public void Dispose()
    {
        _scope.Dispose();
        _root.Dispose();
        SqliteConnection.ClearAllPools();
        try
        {
            System.IO.Directory.Delete(Directory, true);
        }
        catch (IOException)
        {
        }
    }
}
