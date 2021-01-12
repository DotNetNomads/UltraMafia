using System.Threading.Tasks;
using JKang.EventBus;
using UltraMafia.Common.Events;

namespace UltraMafia.Frontend.EventHandlers
{
    public class GamerJoinedEventHandler : IEventHandler<GamerJoinedEvent>
    {
        public Task HandleEventAsync(GamerJoinedEvent @event)
        {
            throw new System.NotImplementedException();
        }
    }
}