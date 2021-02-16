using System.Threading.Tasks;
using Telegram.Bot.Types;

namespace UltraMafia.Frontend.ComandHandlers
{
    public interface INonCommandMessageHandler
    {
        ValueTask HandleMessageAsync(Message message);
    }
}