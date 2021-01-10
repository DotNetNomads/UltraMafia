using System.Threading;
using UltraMafia.DAL.Model;

namespace UltraMafia.Frontend.Telegram
{
    public class RegistrationMessageInfo
    {
        public CancellationTokenSource CancellationTokenSource { get; set; } = null!;
        public int? CurrentMessageId { get; set; }
        public GameSession Session { get; set; } = null!;
    }
}