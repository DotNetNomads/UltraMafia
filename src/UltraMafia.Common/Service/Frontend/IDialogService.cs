using System.Threading.Tasks;
using UltraMafia.Common.GameModel;
using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Service.Frontend
{
    /// <summary>
    /// Handles game dialogs
    /// </summary>
    public interface IDialogService
    {
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
    }
}