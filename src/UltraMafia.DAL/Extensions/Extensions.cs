using System;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using UltraMafia.DAL.Config;

namespace UltraMafia.DAL.Extensions
{
    public static class Extensions
    {
        public static IServiceCollection AddDb(this IServiceCollection services, IConfiguration config)
        {
            var dbSettings = new DbSettings();
            config.GetSection("Db").Bind(dbSettings);
            Log.Debug("Using following settings for DB: host {0}, db {1}", dbSettings.Host, dbSettings.DbName);
            var connectionString =
                $"Server={dbSettings.Host};Port={dbSettings.Port};Database={dbSettings.DbName};Uid={dbSettings.User};Pwd={dbSettings.Password};";
            services.AddDbContext<GameDbContext>(c => c
                .UseMySql(connectionString, new MySqlServerVersion("8.*.*"),
                    a => a.MigrationsAssembly("UltraMafia.App.DAL")));
            return services;
        }

        public static async Task RunMigratorAsync(this IServiceProvider serviceProvider)
        {
            Log.Information("Applying migrations to database...");

            using (var scope = serviceProvider.CreateScope())
            {
                var context = scope.ServiceProvider.GetService<GameDbContext>();
                await context.Database.MigrateAsync();
            }

            Log.Information("Migrations - Done!");
        }
    }
}