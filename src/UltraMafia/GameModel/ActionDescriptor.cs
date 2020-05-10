using System.Collections;
using UltraMafia.DAL.Enums;
using UltraMafia.DAL.Model;

namespace UltraMafia.GameModel
{
    public struct ActionDescriptor
    {
        public ActionDescriptor(GameActions? action = null, GameSessionMember target = null,
            GameSessionMember actionFrom = null)
        {
            Action = action;
            Target = target;
            ActionFrom = actionFrom;
        }

        public GameSessionMember? ActionFrom { get; set; }
        public GameActions? Action { get; set; }
        public GameSessionMember? Target { get; set; }
    }
}