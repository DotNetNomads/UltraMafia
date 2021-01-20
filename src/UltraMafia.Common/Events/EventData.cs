namespace UltraMafia.Common.Events
{
    public record RoomInfo(string ExternalId, string RoomName);

    public record GamerInfo(int ExternalUserId, string UserName, string PersonalRoomId);
}