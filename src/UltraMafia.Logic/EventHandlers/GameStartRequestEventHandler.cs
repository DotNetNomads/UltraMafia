using System.Threading.Tasks;
using JKang.EventBus;
using UltraMafia.Common.Events;

namespace UltraMafia.Logic.EventHandlers
{
    public class GameStartRequestEventHandler : IEventHandler<GameStartRequestEvent>
    {
        public Task HandleEventAsync(GameStartRequestEvent @event)
        {
            throw new System.NotImplementedException();
        }
    }
}