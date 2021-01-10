using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Events
{
    public struct GamerJoinedEvent
    {
        public GamerJoinedEvent(GameSession session, GamerAccount account)
        {
            Session = session;
            Account = account;
        }

        public GameSession Session { get; }
        public GamerAccount Account { get; }
    }
}