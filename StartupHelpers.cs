using System;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UltraMafia.Frontend;

namespace UltraMafia
{
    public static class StartupHelpers
    {
        public static void UseMafiaGame(this IApplicationBuilder app)
        {
            var scope = app.ApplicationServices.CreateScope();
            try
            {
                Console.WriteLine("Starting game...");
                var service = scope.ServiceProvider.GetService<GameService>();
                service.ListenToEvents();
            }
            catch (Exception ex)
            {
                Console.Error.WriteLine(ex.Message);
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
    }
}