using System.Threading.Tasks;
using JKang.EventBus;
using UltraMafia.Common.Events;

namespace UltraMafia.Frontend.EventHandlers
{
    public class GameRegistrationStoppedEventHandler : IEventHandler<GameRegistrationStoppedEvent>
    {
        public Task HandleEventAsync(GameRegistrationStoppedEvent @event)
        {
            throw new System.NotImplementedException();
        }
    }
}