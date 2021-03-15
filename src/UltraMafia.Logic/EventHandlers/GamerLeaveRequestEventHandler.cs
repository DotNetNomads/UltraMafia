using System.Threading.Tasks;
using JKang.EventBus;
using UltraMafia.Common.Events;

namespace UltraMafia.Logic.EventHandlers
{
    public class GamerLeaveRequestEventHandler : IEventHandler<GamerLeaveRequestEvent>
    {
        public Task HandleEventAsync(GamerLeaveRequestEvent @event)
        {
            throw new System.NotImplementedException();
        }
    }
}