using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Events
{
    /// <summary>
    /// Occurs after game session creation
    /// </summary>
    public record GameCreatedEvent(GameSession Session);

    /// <summary>
    /// Occurs when game registration was stopped
    /// </summary>
    public record GameRegistrationStoppedEvent(GameSession Session);

    /// <summary>
    /// Occrus when gamer's request for join was accepted
    /// </summary>
    public record GamerJoinedEvent(GameSession Session, GamerAccount Account);

    /// <summary>
    /// Occurs when gamer left from game
    /// </summary>
    public record GamerLeftEvent(GameSession Session);

    /// <summary>
    /// Occurs when game was started
    /// </summary>
    public record GameStartedEvent(GameSession Session);
}