using UltraMafia.DAL.Model;

namespace UltraMafia.GameModel
{
    public struct ApproveVoteDescriptor
    {
        public GameSessionMember VoiceOwner { get; set; }
        public bool Approve { get; set; }
    }
}