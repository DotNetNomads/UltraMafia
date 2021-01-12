using System;
using System.Threading.Tasks;
using UltraMafia.DAL.Model;

namespace UltraMafia.Common.Service.Frontend
{
    /// <summary>
    /// Handles muting / unmuting logic
    /// <remarks>Implement in the future release</remarks>
    /// </summary>
    public interface IMuteService
    {
        /// <summary>
        /// Mute all in specific room
        /// </summary>
        /// <param name="room">Room identifier</param>
        /// <param name="until">Expiration time</param>
        /// <returns></returns>
        Task MuteAll(GameRoom room, DateTime until);

        /// <summary>
        /// Unmute all in specific room
        /// </summary>
        /// <param name="room">Room identifier</param>
        /// <returns></returns>
        Task UnmuteAll(GameRoom room);
        /// <summary>
        /// Mute specific gamers
        /// </summary>
        /// <param name="room">Room identifier</param>
        /// <param name="gamers">Gamers list</param>
        /// <returns></returns>
        Task MuteSpecificGamers(GameRoom room, GamerAccount[] gamers);
        /// <summary>
        /// Unmute specific gamers
        /// </summary>
        /// <param name="room">Room identifier</param>
        /// <param name="gamers">Gamers list</param>
        /// <returns></returns>
        Task UnmuteSpecificGamers(GameRoom room, GamerAccount[] gamers);
    }
}