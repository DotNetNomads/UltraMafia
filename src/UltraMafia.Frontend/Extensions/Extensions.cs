using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using UltraMafia.Common.Service.Frontend;
using UltraMafia.Frontend.Model.Config;
using UltraMafia.Frontend.Service.Telegram;
using UltraMafia.Frontend.Telegram;

namespace UltraMafia.Frontend.Extensions
{
    public static class Extensions
    {
        public static IServiceCollection AddTelegramFrontend(this IServiceCollection services,
            IConfiguration config)
        {
            var teleSettings = new TelegramFrontendSettings();
            config.GetSection("Frontend").Bind(teleSettings);
            services.AddSingleton(s => teleSettings);
            
            services.AddScoped<IMessageSenderService, MessageSenderService>();
            
            return services;
        }
    }
}