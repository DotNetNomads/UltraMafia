using System;
using UlstraMafia.DAL;
using UltraMafia.DAL.Enums;

namespace UltraMafia.DAL.Model
{
    public class GameSessionLog : BaseEntity
    {
        public int GameSessionId { get; set; }
        public DateTime OccuredAt { get; set; }
        public GameActions Action { get; set; }
        public int GamerIdFrom { get; set; }
        public int? GamerIdTo { get; set; }
        public string Details { get; set; }
    }
}