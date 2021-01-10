using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UltraMafia.Frontend.Telegram;
using UltraMafia.Frontend.Telegram.Config;

namespace UltraMafia.Frontend.Extensions
{
    public static class StartupExtensions
    {
        public static IServiceCollection AddTelegramFrontend(this IServiceCollection services,
            IConfiguration config)
        {
            var teleSettings = new TelegramFrontendSettings();
            config.GetSection("Frontend").Bind(teleSettings);
            services.AddSingleton(s => teleSettings);
            services.AddScoped<IFrontend, TelegramFrontend>();
            return services;
        }
    }
}