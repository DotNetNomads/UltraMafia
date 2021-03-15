using System.Threading.Tasks;
using JKang.EventBus;
using UltraMafia.Common.Events;

namespace UltraMafia.Logic.EventHandlers
{
    public class GameStopRequestEventHandler: IEventHandler<GameStopRequestEvent>
    {
        public Task HandleEventAsync(GameStopRequestEvent @event)
        {
            throw new System.NotImplementedException();
        }
    }
}