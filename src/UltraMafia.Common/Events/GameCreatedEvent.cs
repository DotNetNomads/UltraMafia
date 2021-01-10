using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Events
{
    public struct GameCreatedEvent
    {
        public GameCreatedEvent(GameSession session)
        {
            Session = session;
        }

        public GameSession Session { get; }
    }
}