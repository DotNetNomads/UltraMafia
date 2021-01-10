#nullable enable
using System.Linq;
using System.Text;
using UltraMafia.DAL.Enums;
using UltraMafia.DAL.Model;

namespace UltraMafia.Logic.Extensions
{
    public static class GameServiceExtensions
    {
        public static GameSessionMember? GetDoctor(this GameSession session) => session.GameMembers
            .FirstOrDefault(gm => gm.Role == GameRoles.Doctor && !gm.IsDead);

        public static GameSessionMember[] GetAliveMembers(this GameSession session) =>
            session.GameMembers.Where(gm => !gm.IsDead).ToArray();

        public static GameSessionMember? GetCop(this GameSession session) =>
            session.GameMembers.FirstOrDefault(gm => gm.Role == GameRoles.Cop && !gm.IsDead);

        public static GameSessionMember[] GetMafia(this GameSession session) =>
            session.GameMembers.Where(gm => gm.Role == GameRoles.Mafia && !gm.IsDead).ToArray();

        public static string GetMembersInfo(this GameSession session, bool roles = false, bool showRoleIfDead = false)
        {
            var sb = new StringBuilder();
            foreach (var member in session.GameMembers)
            {
                var infoStringBuilder = new StringBuilder();
                if (roles || member.IsDead && showRoleIfDead)
                    infoStringBuilder.Append($":  <i>{member.Role.GetRoleName()}</i>");
                if (member.IsDead) infoStringBuilder.Append("  ☠️");
                if (member.IsWin) infoStringBuilder.Append("  🏆");

                sb.AppendLine($"- {member.GamerAccount.NickName}{infoStringBuilder}\n");
            }

            return sb.ToString();
        }

        public static string GetRoleName(this GameRoles role)
        {
            return role switch
            {
                GameRoles.Citizen => "Мирный житель",
                GameRoles.Doctor => "Доктор",
                GameRoles.Cop => "Коммисар",
                GameRoles.Mafia => "Мафиози",
                _ => ""
            };
        }
    }
}