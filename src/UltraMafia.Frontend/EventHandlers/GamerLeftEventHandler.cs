using System.Threading.Tasks;
using JKang.EventBus;
using UltraMafia.Common.Events;

namespace UltraMafia.Frontend.EventHandlers
{
    public class GamerLeftEventHandler : IEventHandler<GamerLeftEvent>
    {
        public Task HandleEventAsync(GamerLeftEvent @event)
        {
            throw new System.NotImplementedException();
        }
    }
}