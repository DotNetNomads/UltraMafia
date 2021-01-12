namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occurs when new game session requested from frontend
    /// </summary>
    public class GameCreationRequestEvent
    {
        public GameCreationRequestEvent(int roomId, int gamerAccountId)
        {
            RoomId = roomId;
            GamerAccountId = gamerAccountId;
        }

        /// <summary>
        /// Room identifier
        /// </summary>
        public int RoomId { get; }
        /// <summary>
        /// New game creator's account identifier
        /// </summary>
        public int GamerAccountId { get; }
    }
}