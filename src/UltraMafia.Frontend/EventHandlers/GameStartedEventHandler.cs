using System.Threading.Tasks;
using JKang.EventBus;
using UltraMafia.Common.Events;

namespace UltraMafia.Frontend.EventHandlers
{
    public class GameStartedEventHandler: IEventHandler<GameStartedEvent>
    {
        public Task HandleEventAsync(GameStartedEvent @event)
        {
            throw new System.NotImplementedException();
        }
    }
}