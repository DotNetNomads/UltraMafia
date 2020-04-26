using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using UltraMafia.DAL;
using UltraMafia.Frontend;
using UltraMafia.Frontend.Telegram;

namespace UltraMafia.Helpers
{
    public static class StartupHelpers
    {
        public static void UseMafiaGame(this IApplicationBuilder app)
        {
            var scope = app.ApplicationServices.CreateScope();
            try
            {
                Log.Information("Starting mafia game...");
                var service = scope.ServiceProvider.GetService<GameService>();
                service.CheckDatabase();
                service.ListenToEvents();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error occured");
                scope.Dispose();
            }
        }

        public static void AddMafiaGame(this IServiceCollection services, IConfiguration config)
        {
            var teleSettings = new TelegramFrontendSettings();
            config.GetSection("Frontend").Bind(teleSettings);
            var gameSettings = new GameSettings();
            config.GetSection("Game").Bind(gameSettings);
            services.AddSingleton(s => teleSettings);
            services.AddSingleton(s => gameSettings);
            services.AddScoped<IFrontend, TelegramFrontend>();
            services.AddScoped<GameService>();
        }

        public static void AddDb(this IServiceCollection services, IConfiguration config)
        {
            var dbSettings = new DbSettings();
            config.GetSection("Db").Bind(dbSettings);
            Log.Debug("Using following settings for DB: host {0}, db {1}", dbSettings.Host, dbSettings.DbName);
            var connectionString =
                $"Server={dbSettings.Host};Port={dbSettings.Port};Database={dbSettings.DbName};Uid={dbSettings.User};Pwd={dbSettings.Password};";
            services.AddDbContext<GameDbContext>(c => c
                .UseMySql(connectionString, a => a.MigrationsAssembly("UltraMafia.DAL")));
        }
    }
}