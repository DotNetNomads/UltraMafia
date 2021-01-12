using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occurs after game session creation
    /// </summary>
    public class GameCreatedEvent
    {
        public GameCreatedEvent(GameSession session) => Session = session;

        /// <summary>
        /// New game session instance
        /// </summary>
        public GameSession Session { get; }
    }
}