using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occurs when game was started
    /// </summary>
    public class GameStartedEvent
    {
        public GameStartedEvent(GameSession session) => Session = session;

        /// <summary>
        /// Started game session instance
        /// </summary>
        public GameSession Session { get; }
    }
}