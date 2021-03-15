using System.Threading.Tasks;
using JKang.EventBus;
using UltraMafia.Common.Events;

namespace UltraMafia.Frontend.EventHandlers
{
    public class GameCreatedEventHandler: IEventHandler<GameCreatedEvent>
    {
        public Task HandleEventAsync(GameCreatedEvent @event)
        {
            throw new System.NotImplementedException();
        }
    }
}