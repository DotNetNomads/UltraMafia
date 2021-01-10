using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Events
{
    public class GameRegistrationStoppedEvent
    {
        public GameRegistrationStoppedEvent(GameSession session)
        {
            Session = session;
        }

        private GameSession Session { get; }
    }
}