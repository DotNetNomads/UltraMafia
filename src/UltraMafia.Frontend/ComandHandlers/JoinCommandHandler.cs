using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;

namespace UltraMafia.Frontend.ComandHandlers
{
    public class JoinCommandHandler : ICommandHandler
    {
        private ILogger<JoinCommandHandler> _logger;

        public JoinCommandHandler(ILogger<JoinCommandHandler> logger)
        {
            _logger = logger;
        }

        public ValueTask HandleCommandAsync(string[] arguments, Chat messageChat, User messageFrom)
        {
            _logger.LogDebug("Processing join command from {UserId}", messageFrom.Id);
            if (messageChat.Type != ChatType.Private)
                return ValueTask.CompletedTask;
            if (!arguments.Any() || !int.TryParse(arguments[0], out var roomId))
                throw new Exception(
                    "Невозможно зарегистрировать в игре. Отсутствует номер игровой комнаты. Сначала нажмите на кнопку в общем чате.");

            int gamerAccountId;


            OnGameJoinRequest(roomId, gamerAccountId);
        }
    }
}