using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using UltraMafia.Common.Service.Frontend;
using UltraMafia.DAL.Model;
using UltraMafia.Frontend.Telegram;

namespace UltraMafia.Frontend.Service.Telegram
{
    public class MessageSenderService : IMessageSenderService
    {
        private readonly ITelegramBotClient _bot;

        public MessageSenderService(ITelegramBotClient bot) => _bot = bot;

        public async Task SendMessageToGamer(GamerAccount gamer, string message)
        {
            if (gamer.PersonalRoomId == null || gamer.PersonalRoomId == "0")
                return;

            await _bot.LockAndDo(() => _bot.SendTextMessageAsync(gamer.PersonalRoomId, message, ParseMode.Html));
        }

        public Task SendMessageToRoom(GameRoom room, string message, bool important = false) =>
            _bot.LockAndDo(async () =>
            {
                var messageObj = await _bot.SendTextMessageAsync(room.ExternalRoomId, message, ParseMode.Html);
                if (important)
                {
                    await Task.Delay(100);
                    await _bot.PinMessageIfAllowed(messageObj, CancellationToken.None);
                }
            });
    }
}