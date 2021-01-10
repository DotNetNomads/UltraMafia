using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using UltraMafia.DAL.Extensions;

namespace UltraMafia.Logic.Service
{
    /// <summary>
    /// Actualizes database state after reboot or failure
    /// </summary>
    public interface IDatabaseActualizationService
    {
        Task CheckDatabaseAsync();
    }

    public class DatabaseActualizationService : IDatabaseActualizationService
    {
        private readonly IServiceProvider _serviceProvider;

        public DatabaseActualizationService(IServiceProvider serviceProvider)
        {
            _serviceProvider = serviceProvider;
        }

        public async Task CheckDatabaseAsync()
        {
            using var dbContextAccessor = _serviceProvider.GetDbContext();
            Log.Information("Cleaning up database...");
            await dbContextAccessor.DbContext.Database.ExecuteSqlRawAsync(
                "update `GameSessions` set `State`='ForceFinished' where `State`='Playing'");
            Log.Information("Database is cleaned from old sessions");
        }
    }
}