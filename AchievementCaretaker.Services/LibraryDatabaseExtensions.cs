/* AchievementCaretaker
 *
 * Copyright (c) 2026 Yaroslav V Tatarenko
 *
 * See LICENSE in the repository root.
 */

using System;
using System.Data;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.DependencyInjection;

namespace AchievementCaretaker.Services;

/// <summary>Registers the library database.</summary>
public static class LibraryDatabaseExtensions
{
    /// <summary>
    /// Registers the library database at <paramref name="databasePath"/>. The connection and
    /// the command are scoped, so each window has its own and nothing opens one by a static
    /// call; the state that says where the file is, is a singleton.
    /// </summary>
    /// <remarks>
    /// The tables are made by <see cref="MigrateLibraryDatabase"/>, once, at start.
    /// </remarks>
    public static IServiceCollection AddLibraryDatabase(this IServiceCollection services, string databasePath)
    {
        services.AddSingleton(new DatabaseState(databasePath));
        services.AddScoped<IDbConnection>(provider => new SqliteConnection(provider.GetRequiredService<DatabaseState>().ConnectionString));
        services.AddScoped<IDbCommand>(provider => provider.GetRequiredService<IDbConnection>().CreateCommand());
        services.AddScoped<IGameRepository, GameRepository>();
        services.AddScoped<IDatabaseMigrator, DatabaseMigrator>();
        services.AddScoped<IDatabaseRelocator, DatabaseRelocator>();
        return services;
    }

    /// <summary>
    /// Prepares the database through a scope of its own. A database that cannot be prepared is
    /// recorded in <see cref="DatabaseState.Error"/> and not thrown: the application opens
    /// without a cache, which is what <see cref="IGameRepository.IsAvailable"/> reports.
    /// </summary>
    public static void MigrateLibraryDatabase(this IServiceProvider services)
    {
        using var scope = services.CreateScope();
        try
        {
            scope.ServiceProvider.GetRequiredService<IDatabaseMigrator>().Migrate();
        }
        catch (Exception e)
        {
            scope.ServiceProvider.GetRequiredService<DatabaseState>().Error = e.Message;
        }
    }
}
