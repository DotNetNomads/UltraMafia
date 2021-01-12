namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occurs when frontend are asking to stop a game
    /// </summary>
    public class GameStopRequestEvent
    {
        public GameStopRequestEvent(int gamerAccountId, int roomId)
        {
            GamerAccountId = gamerAccountId;
            RoomId = roomId;
        }

        /// <summary>
        /// Game room identifier
        /// </summary>
        public int RoomId { get; }

        /// <summary>
        /// Game account identifier of request author
        /// </summary>
        public int GamerAccountId { get; }
    }
}