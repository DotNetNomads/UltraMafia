namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occurs when new game session requested from frontend
    /// </summary>
    public record GameCreationRequestEvent(int RoomId, int GamerAccountId);

    /// <summary>
    /// Occurs when gamer sent request for join to game
    /// </summary>
    public record GamerJoinRequestEvent(int RoomId, int GamerAccountId);

    /// <summary>
    /// Occurs when gamer requests for leaving from game
    /// </summary>
    public record GamerLeaveRequestEvent(RoomInfo RoomInfo, GamerInfo GamerInfo);

    /// <summary>
    /// Occurs when frontend asks to start a game
    /// </summary>
    public record GameStartRequestEvent(int RoomId);

    /// <summary>
    /// Occurs when frontend are asking to stop a game
    /// </summary>
    public record GameStopRequestEvent(GamerInfo GamerInfo, RoomInfo RoomInfo);
}