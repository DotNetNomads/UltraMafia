using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using UltraMafia.DAL.Extensions;
using UltraMafia.Frontend.Extensions;
using UltraMafia.Logic.Extensions;

// Configuration
var configuration = new ConfigurationBuilder()
    .AddJsonFile("settings.json")
    .AddEnvironmentVariables("mafia_")
    .Build();
Log.Logger = new LoggerConfiguration()
    .Enrich.WithThreadId()
    .Enrich.WithThreadName()
    .ReadFrom.Configuration(configuration)
    .WriteTo.Console(outputTemplate:
        "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] {Message:lj} {Properties}{NewLine}{Exception}")
    .CreateLogger();

// IoC
var serviceProvider = new ServiceCollection()
    .AddLogging()
    .AddDb(configuration)
    .AddMemoryCache()
    .AddTelegramFrontend(configuration)
    .AddMafiaGame(configuration)
    .AddEventBus(builder =>
    {
    })
    .BuildServiceProvider();
// Linking Serilog as a default logging provider
serviceProvider
    .GetRequiredService<ILoggerFactory>()
    .AddSerilog();

// ------ //

// Migrations branch (if CLI argument was provided)
if (args.Length >= 1 && args[0] == "/seed")
{
    await serviceProvider.RunMigratorAsync();
    return;
}

serviceProvider.RunMafiaGame();
await Task.Delay(-1);