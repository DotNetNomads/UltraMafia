namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occurs when frontend asks to start a game
    /// </summary>
    public class GameStartRequestEvent
    {
        public GameStartRequestEvent(int roomId) =>
            RoomId = roomId;

        /// <summary>
        /// Game room identifier
        /// </summary>
        public int RoomId { get; }
    }
}