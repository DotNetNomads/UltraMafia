namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occurs when gamer sent request for join to game
    /// </summary>
    public class GamerJoinRequestEvent
    {
        public GamerJoinRequestEvent(int roomId, int gamerAccountId)
        {
            RoomId = roomId;
            GamerAccountId = gamerAccountId;
        }

        /// <summary>
        /// Gamer's room identifier
        /// </summary>
        public int RoomId { get; }

        /// <summary>
        /// Gamer's account identifier
        /// </summary>
        public int GamerAccountId { get; }
    }
}