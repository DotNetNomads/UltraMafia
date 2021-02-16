using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace UltraMafia.Frontend.ComandHandlers
{
    public interface ICommandHandler
    {
        ValueTask HandleCommandAsync(string[] arguments, Chat messageChat, User messageFrom);
    }
}