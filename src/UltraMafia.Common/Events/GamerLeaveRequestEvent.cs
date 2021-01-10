namespace UltraMafia.Common.Events
{
    public struct GamerLeaveRequestEvent
    {
        public GamerLeaveRequestEvent(int roomId, int gamerId)
        {
            RoomId = roomId;
            GamerId = gamerId;
        }

        public int RoomId { get; }
        public int GamerId { get; }
    }
}