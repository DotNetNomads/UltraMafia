using UlstraMafia.DAL;

namespace UltraMafia.DAL.Model
{
    public class GameRoom : BaseEntity
    {
        public string ExternalRoomId { get; set; }
        public string RoomName { get; set; }
    }
}