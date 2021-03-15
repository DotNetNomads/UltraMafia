using System;
using System.Collections.Generic;
using UltraMafia.DAL.Enums;

namespace UltraMafia.DAL.Model
{
    public class GameSession : BaseEntity
    {
        public int RoomId { get; set; }
        public GameRoom Room { get; set; }
        public DateTime StartedOn { get; set; }
        public DateTime FinishedOn { get; set; }
        public GameSessionStates State { get; set; }
        public List<GameSessionMember> GameMembers { get; set; } = new();
        public int CreatedByGamerAccountId { get; set; }
        public GamerAccount CreatedByGamerAccount { get; set; }
    }
}