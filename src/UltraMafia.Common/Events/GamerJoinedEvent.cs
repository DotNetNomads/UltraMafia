using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occrus when gamer's request for join was accepted
    /// </summary>
    public class GamerJoinedEvent
    {
        public GamerJoinedEvent(GameSession session, GamerAccount account)
        {
            Session = session;
            Account = account;
        }

        /// <summary>
        /// Game session where gamer was joined
        /// </summary>
        public GameSession Session { get; }

        /// <summary>
        /// Gamers account instance
        /// </summary>
        public GamerAccount Account { get; }
    }
}