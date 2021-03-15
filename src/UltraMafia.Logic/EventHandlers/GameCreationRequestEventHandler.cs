using System.Threading.Tasks;
using JKang.EventBus;
using UltraMafia.Common.Events;

namespace UltraMafia.Logic.EventHandlers
{
    public class GameCreationRequestEventHandler: IEventHandler<GameCreationRequestEvent>
    {
        public Task HandleEventAsync(GameCreationRequestEvent @event)
        {
            throw new System.NotImplementedException();
        }
    }
}