using System.Threading.Tasks;
using UltraMafia.Common.GameModel;
using UltraMafia.Common.Service.Frontend;
using UltraMafia.DAL.Model;

namespace UltraMafia.Frontend.Service.Telegram
{
    public class DialogService : IDialogService
    {
        public Task<ActionDescriptor> AskDoctorForAction(GameSessionMember doctor, GameSessionMember[] membersToSelect)
        {
            throw new System.NotImplementedException();
        }

        public Task<ActionDescriptor> AskCopForAction(GameSessionMember? cop, GameSessionMember[] aliveMembers)
        {
            throw new System.NotImplementedException();
        }

        public Task<string?> GetLastWords(GamerAccount gamerAccount)
        {
            throw new System.NotImplementedException();
        }

        public Task<ActionDescriptor> AskMafiaForAction(GameSessionMember mafia, GameSessionMember[] availableGamers)
        {
            throw new System.NotImplementedException();
        }
    }
}