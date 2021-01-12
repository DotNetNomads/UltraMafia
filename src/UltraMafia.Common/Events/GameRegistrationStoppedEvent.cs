using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occurs when game registration was stopped
    /// </summary>
    public class GameRegistrationStoppedEvent
    {
        public GameRegistrationStoppedEvent(GameSession session) => Session = session;

        /// <summary>
        /// Stopped game's session instance
        /// </summary>
        private GameSession Session { get; }
    }
}