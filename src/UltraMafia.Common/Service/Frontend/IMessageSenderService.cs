using System.Threading.Tasks;
using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Service.Frontend
{
    /// <summary>
    /// Handles message sending to specific gamer or room
    /// </summary>
    public interface IMessageSenderService
    {
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
    }
}