using System;
using UlstraMafia.DAL;
using UltraMafia.DAL.Enums;

namespace UltraMafia.DAL.Model
{
    public class GameSession : BaseEntity
    {
        public string RoomId { get; set; }
        public DateTime StartedOn { get; set; }
        public DateTime FinishedOn { get; set; }
        public GameSessionStates State { get; set; }
    }
}