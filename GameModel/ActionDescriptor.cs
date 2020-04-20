using System.Collections;
using UltraMafia.DAL.Enums;
using UltraMafia.DAL.Model;

namespace UltraMafia.GameModel
{
    public struct ActionDescriptor
    {
        public ActionDescriptor(GameActions? action = null, GameSessionMember target = null)
        {
            Action = action;
            Target = target;
        }

        public GameActions? Action { get; set; }
        public GameSessionMember? Target { get; set; }
    }
}