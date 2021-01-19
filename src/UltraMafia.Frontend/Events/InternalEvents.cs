namespace UltraMafia.Frontend.Events
{
    /// <summary>
    /// Occurs when bot receives vote  from gamer
    /// </summary>
    public record VoteAnswerReceivedEvent(string RoomId, string UserId, string Voice, string CallbackQueryId);
}