using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Events
{
    public struct GamerLeftEvent
    {
        public GamerLeftEvent(GameSession session)
        {
            Session = session;
        }

        public GameSession Session { get; }
    }
}