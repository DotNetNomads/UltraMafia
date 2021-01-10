using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Events
{
    public struct GameStartedEvent
    {
        public GameStartedEvent(GameSession session)
        {
            Session = session;
        }

        public GameSession Session { get; }
    }
}