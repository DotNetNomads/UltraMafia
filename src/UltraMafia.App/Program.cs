using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
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
    .ReadFrom.Configuration(configuration)
    .WriteTo.Console()
    .CreateLogger();

// IoC
var serviceProvider = new ServiceCollection()
    .AddDb(configuration)
    .AddTelegramFrontend(configuration)
    .AddMafiaGame(configuration)
    .BuildServiceProvider();

// ------ //

// Migrations detecting
if (args.Length >= 1 && args[0] == "/seed")
{
    await serviceProvider.RunMigratorAsync();
    return;
}

serviceProvider.RunMafiaGame();
await Task.Delay(-1);