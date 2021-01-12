using System.Threading.Tasks;
using UltraMafia.Common.GameModel;
using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Service.Frontend
{
    /// <summary>
    /// Handles voting logic 
    /// </summary>
    public interface IVoteService
    {
        /// <summary>
        /// Creates Lynch vote and returns results
        /// </summary>
        /// <param name="sessionRoom">Current game room</param>
        /// <param name="allowedMembers">Allowed to vote members</param>
        /// <returns>List of members voices</returns>
        Task<VoteDescriptor[]> CreateLynchVoteAndReceiveResults(GameRoom sessionRoom,
            GameSessionMember[] allowedMembers);

        /// <summary>
        /// Creates Lynch approval vote and returns result
        /// </summary>
        /// <param name="sessionRoom">Current game room</param>
        /// <param name="allowedMembers">Allowed to vote members</param>
        /// <param name="gamerForLynch">Target member of Lynch</param>
        /// <returns>List of members voices</returns>
        Task<ApproveVoteDescriptor[]> CreateLynchApprovalVote(GameRoom sessionRoom, GameSessionMember[] allowedMembers,
            GameSessionMember gamerForLynch);
    }
}