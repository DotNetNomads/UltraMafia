using System.Threading.Tasks;
using JKang.EventBus;
using UltraMafia.Common.Events;

namespace UltraMafia.Logic.EventHandlers
{
    public class GamerJoinRequestEventHandler: IEventHandler<GamerJoinRequestEvent>
    {
        public Task HandleEventAsync(GamerJoinRequestEvent @event)
        {
            throw new System.NotImplementedException();
        }
    }
}