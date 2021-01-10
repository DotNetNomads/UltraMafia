using System;
using System.Threading.Tasks;
using UltraMafia.Common.GameModel;
using UltraMafia.DAL.Model;

namespace UltraMafia.Frontend
{
    public interface IFrontend
    {
        /// <summary>
        /// Enables frontend to receive requests
        /// </summary>
        void ActivateFrontend();

        Task MuteAll(GameRoom room, DateTime until);
        Task UnmuteAll(GameRoom room);

        /// <summary>
        /// Sends message to specific gamer
        /// </summary>
        /// <param name="gamer">Gamer</param>
        /// <param name="message">Message content</param>
        /// <returns></returns>
        Task SendMessageToGamer(GamerAccount gamer, string message);

        /// <summary>
        /// Sends message to specific room
        /// </summary>
        /// <param name="room">Room</param>
        /// <param name="message">Message content</param>
        /// <param name="important">Indicates that message is important</param>
        /// <returns></returns>
        Task SendMessageToRoom(GameRoom room, string message, bool important = false);

        Task MuteSpecificGamers(GameRoom room, GamerAccount[] gamers);
        Task UnmuteSpecificGamers(GameRoom room, GamerAccount[] gamers);

        /// <summary>
        /// Event that called when user tries join a current game session
        /// </summary>
        event Action<(int roomId, int gamerId)> GameJoinRequest;

        /// <summary>
        /// Event that called when user tries create a game session
        /// </summary>
        event Action<(int roomId, int gamerId)> GameCreationRequest;

        /// <summary>
        /// Event that called when user tries stop the current game
        /// </summary>
        event Action<(int roomId, int gamerId)> GameStopRequest;

        /// <summary>
        /// Event that called when user tries to start a game session
        /// </summary>
        /// <remarks>roomId will be passed as an argument</remarks>
        event Action<int> GameStartRequest;
        
        /// <summary>
        /// Event that called when user tries to leave a game session
        /// </summary>
        /// <remarks>roomId and gamerId will be passed as an argument</remarks>

        event Action<(int roomId, int gamerId)> GameLeaveRequest;

        /// <summary>
        /// Asks doctor for action
        /// </summary>
        /// <param name="doctor">Doctor gamer</param>
        /// <param name="membersToSelect">Gamers that allowed to be action target</param>
        /// <returns>Selected action info</returns>
        Task<ActionDescriptor> AskDoctorForAction(GameSessionMember doctor, GameSessionMember[] membersToSelect);

        /// <summary>
        /// Asks cop for action
        /// </summary>
        /// <param name="cop">Cop gamer</param>
        /// <param name="aliveMembers">Gamers that allowed to be action target</param>
        /// <returns>Selected action info</returns>
        Task<ActionDescriptor> AskCopForAction(GameSessionMember? cop, GameSessionMember[] aliveMembers);

        /// <summary>
        /// Asks died gamer for last words
        /// </summary>
        /// <param name="gamerAccount">Died gamer</param>
        /// <returns>Last words if provided</returns>
        Task<string?> GetLastWords(GamerAccount gamerAccount);

        /// <summary>
        /// Asks mafia for action
        /// </summary>
        /// <param name="mafia">Mafia member</param>
        /// <param name="availableGamers">Gamers that allowed to be action target</param>
        /// <returns>Selected action information</returns>
        Task<ActionDescriptor> AskMafiaForAction(GameSessionMember mafia, GameSessionMember[] availableGamers);

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

        /// <summary>
        /// Process game session creation and display registration interface
        /// </summary>
        /// <param name="session">New created game session</param>
        void OnGameSessionCreated(GameSession session);

        /// <summary>
        /// Process gamer join in the game and update registration interface
        /// </summary>
        /// <param name="session">Game session</param>
        /// <param name="account">Gamer account</param>
        void OnGamerJoined(GameSession session, GamerAccount account);
        
        /// <summary>
        /// Process when gamer decided to leave current session
        /// </summary>
        /// <param name="session">Game session</param>
        void OnGamerLeft(GameSession session);

        /// <summary>
        /// Process game started state
        /// </summary>
        /// <param name="session">Game session</param>
        void OnGameStarted(GameSession session);

        /// <summary>
        /// Process game registration stopped state
        /// </summary>
        /// <param name="session">Stopped game session</param>
        /// <returns></returns>
        void OnGameRegistrationStopped(GameSession session);
    }
}