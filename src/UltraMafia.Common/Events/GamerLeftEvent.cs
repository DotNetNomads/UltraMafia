using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occurs when gamer left from game
    /// </summary>
    public class GamerLeftEvent
    {
        public GamerLeftEvent(GameSession session) => Session = session;

        /// <summary>
        /// Game session where leave occured
        /// </summary>
        public GameSession Session { get; }
    }
}