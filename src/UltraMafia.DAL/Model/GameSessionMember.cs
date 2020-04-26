using UlstraMafia.DAL;
using UltraMafia.DAL.Enums;

namespace UltraMafia.DAL.Model
{
    public class GameSessionMember : BaseEntity
    {
        public int GameSessionId { get; set; }
        public GameSession GameSession { get; set; }
        public int GamerAccountId { get; set; }
        public GamerAccount GamerAccount { get; set; }
        public bool IsDead { get; set; }
        public bool IsWin { get; set; }
        public GameRoles Role { get; set; }
    }
}