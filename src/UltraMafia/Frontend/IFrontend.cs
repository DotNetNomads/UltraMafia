using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using UltraMafia.DAL.Enums;
using UltraMafia.DAL.Model;
using UltraMafia.GameModel;

namespace UltraMafia.Frontend
{
    public interface IFrontend
    {
        void EnableGame();
        Task MuteAll(GameRoom room, DateTime until);
        Task UnmuteAll(GameRoom room);
        Task SendMessageToGamer(GamerAccount gamer, string message);
        Task SendMessageToRoom(GameRoom room, string message, bool important = false);
        Task MuteSpecificGamers(GameRoom room, GamerAccount[] gamers);
        Task UnmuteSpecificGamers(GameRoom room, GamerAccount[] gamers);

        Func<(GameRoom room, GamerAccount gamerFrom, GameActions action, GamerAccount target), Task> ActionHandler
        {
            get;
            set;
        }

        Func<GameRoom, GamerAccount, Task<GameSession>> RegistrationHandler { get; set; }
        Func<GameRoom, GamerAccount, Task<GameSession>> GameCreationHandler { get; set; }
        Func<GameRoom, GamerAccount, Task<GameSession>> StopGameHandler { get; set; }
        Func<GameRoom, Task<GameSession>> GameStartHandler { get; set; }
        Func<GamerAccount, string, Task> MessageHandler { get; set; }
        Task<ActionDescriptor> AskDoctorForAction(GameSessionMember doctor, GameSessionMember[] membersToSelect);
        Task<ActionDescriptor> AskCopForAction(GameSessionMember? cop, GameSessionMember[] aliveMembers);
        Task<string?> GetLastWords(GamerAccount gamerAccount);
        Task<ActionDescriptor> AskMafiaForAction(GameSessionMember mafia, GameSessionMember[] availableGamers);

        Task<VoteDescriptor[]> CreateLynchVoteAndReceiveResults(GameRoom sessionRoom,
            GameSessionMember[] getAliveMembers);

        Task<ApproveVoteDescriptor[]> CreateLynchApprovalVote(GameRoom sessionRoom, GameSessionMember[] allowedMembers,
            GameSessionMember gamerForLynch);
    }
}