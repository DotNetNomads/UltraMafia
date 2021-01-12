namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occurs when gamer requests for leaving from game
    /// </summary>
    public class GamerLeaveRequestEvent
    {
        public GamerLeaveRequestEvent(int roomId, int gamerId)
        {
            RoomId = roomId;
            GamerAccountId = gamerId;
        }

        /// <summary>
        /// Gamer's room identifier
        /// </summary>
        public int RoomId { get; }

        /// <summary>
        /// Gamer's identifier
        /// </summary>
        public int GamerAccountId { get; }
    }
}