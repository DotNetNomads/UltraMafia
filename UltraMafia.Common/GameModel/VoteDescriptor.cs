using UltraMafia.DAL.Model;

namespace UltraMafia.Common.GameModel
{
    public struct VoteDescriptor
    {
        public GameSessionMember VoiceOwner { get; set; }
        public GameSessionMember VoiceTarget { get; set; }
    }
}