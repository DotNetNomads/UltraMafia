using System;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using UltraMafia.Common.Config;
using UltraMafia.Logic.Service;

namespace UltraMafia.Logic.Extensions
{
    public static class Extensions
    {
        public static void RunMafiaGame(this IServiceProvider serviceProvider)
        {
            var scope = serviceProvider.CreateScope();
            try
            {
                var service = scope.ServiceProvider.GetRequiredService<IDatabaseActualizationService>();
                service.CheckDatabaseAsync();
                Log.Information("Starting mafia game...");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error occured");
                scope.Dispose();
            }
        }

        public static IServiceCollection AddMafiaGame(this IServiceCollection services, IConfiguration config)
        {
            var gameSettings = new GameSettings();
            config.GetSection("Game").Bind(gameSettings);
            services.AddSingleton(s => gameSettings);

            services.AddScoped<GameService>();
            services.AddScoped<IDatabaseActualizationService, DatabaseActualizationService>();

            return services;
        }
    }
}