using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Telegram.Bot;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using UltraMafia.Common.GameModel;
using UltraMafia.Common.Service.Frontend;
using UltraMafia.DAL.Model;
using UltraMafia.Frontend.Telegram;
using static System.Array;

namespace UltraMafia.Frontend.Service.Telegram
{
    public class VoteService : IVoteService
    {
        private readonly ITelegramBotClient _bot;

        public VoteService(ITelegramBotClient bot) => _bot = bot;

        public async Task<VoteDescriptor[]> CreateLynchVoteAndReceiveResults(GameRoom sessionRoom,
            GameSessionMember[] allowedMembers)
        {
            // creating vote
            var variants = allowedMembers
                .Select(m => (m.GamerAccount.NickName, m.GamerAccount.IdExternal)).ToArray();
            var allowedToVoteUserIds = new Dictionary<string, string>();
            foreach (var gameSessionMember in allowedMembers)
                if (!allowedToVoteUserIds.ContainsKey(gameSessionMember.GamerAccount.IdExternal))
                    allowedToVoteUserIds.Add(gameSessionMember.GamerAccount.IdExternal,
                        gameSessionMember.GamerAccount.NickName);

            var telegramVote = new TelegramVote(variants, "<i>Кого будем вешать сегодня?</i> 🎲", allowedToVoteUserIds,
                request =>
                    request.userId != request.voice);
            await CreateVote(telegramVote, sessionRoom.ExternalRoomId);
            const int maxTries = 6;
            var canVoteCount = allowedToVoteUserIds.Count;
            var voices = Empty<TelegramVoiceItem>();
            for (var currentTry = 0; currentTry < maxTries; currentTry++)
            {
                await Task.Delay(10000);
                voices = telegramVote.GetVoices();
                if (voices.Length == canVoteCount)
                    break;
            }

            await FinishVote(telegramVote, sessionRoom.ExternalRoomId);

            return voices.Select(v => new VoteDescriptor
            {
                VoiceOwner = allowedMembers.First(g => g.GamerAccount.IdExternal == v.UserId),
                VoiceTarget = allowedMembers.First(g => g.GamerAccount.IdExternal == v.Voice)
            }).ToArray();
        }

        public async Task<ApproveVoteDescriptor[]> CreateLynchApprovalVote(GameRoom sessionRoom,
            GameSessionMember[] allowedMembers,
            GameSessionMember lynchTarget)
        {
            // creating vote
            var variants = new[] {("👍", "yes"), ("👎", "no")};
            var allowedToVoteUserIds = new Dictionary<string, string>();
            foreach (var gameSessionMember in allowedMembers)
                if (!allowedToVoteUserIds.ContainsKey(gameSessionMember.GamerAccount.IdExternal))
                    allowedToVoteUserIds.Add(gameSessionMember.GamerAccount.IdExternal,
                        gameSessionMember.GamerAccount.NickName);

            var telegramVote = new TelegramVote(variants,
                $"<i>Вешаем <b>{lynchTarget.GamerAccount.NickName}</b>?</i> 🎲",
                allowedToVoteUserIds,
                request =>
                    request.userId != request.voice);
            await CreateVote(telegramVote, sessionRoom.ExternalRoomId);
            const int maxTries = 3;
            var canVoteCount = allowedToVoteUserIds.Count;
            var voices = new TelegramVoiceItem[0];
            for (var currentTry = 0; currentTry < maxTries; currentTry++)
            {
                await Task.Delay(10000);
                voices = telegramVote.GetVoices();
                if (voices.Length == canVoteCount)
                    break;
            }

            await FinishVote(telegramVote, sessionRoom.ExternalRoomId);

            return voices.Select(v => new ApproveVoteDescriptor
            {
                VoiceOwner = allowedMembers.First(g => g.GamerAccount.IdExternal == v.UserId),
                Approve = v.Voice switch
                {
                    "no" => false,
                    "yes" => true,
                    // _ => throw new InvalidOperationException("Lynch answer is incorrect")
                    _ => false
                }
            }).ToArray();
        }

        private async Task FinishVote(TelegramVote telegramVote, string roomId)
        {
            TelegramFrontendExtensions.DeleteVote(roomId);
            await UpdateVote(roomId, telegramVote, true);
        }

        private async Task CreateVote(TelegramVote telegramVote, string roomId)
        {
            TelegramFrontendExtensions.AddVote(roomId, telegramVote);
            await UpdateVote(roomId, telegramVote);
        }

        private async Task UpdateVote(string roomId, TelegramVote telegramVote, bool finish = false)
        {
            var voices = telegramVote.GetVoices();
            var voicesInfo = new StringBuilder();
            var usersAndVoices = (from voice in voices
                join user in telegramVote.AllowedToPassVoteUsersIds on voice.UserId equals user.Key
                select new {voice = voice.Voice, userName = user.Value}).ToList();
            foreach (var (uiName, internalName) in telegramVote.Variants)
            {
                var voiceInfo = usersAndVoices.Where(u => u.voice == internalName).Select(u => u.userName).ToList();
                voicesInfo.AppendLine(
                    $"- <b>{uiName}</b>: {(voiceInfo.Any() ? string.Join(", ", voiceInfo) : "нет голосов")}.\n");
            }

            var messageId = telegramVote.MessageId;
            var finalText = $"<b>Голосование</b>\n{telegramVote.Text}\n\n{voicesInfo}";
            if (finish)
            {
                await _bot.LockAndDo(() => _bot.EditMessageTextAsync(roomId, messageId.Value,
                    $"{finalText}\n<b>Голосование завершено!</b>", ParseMode.Html, false,
                    null));
                return;
            }

            var buttons = telegramVote
                .Variants
                .Select(variant => new List<InlineKeyboardButton>
                {
                    new()
                    {
                        Text = $"{variant.uiName} ({voices.Count(v => v.Voice == variant.internalName)})",
                        CallbackData = $"vote-{variant.internalName}"
                    }
                })
                .ToArray();
            await _bot.LockAndDo(async () =>
            {
                if (messageId == null)
                {
                    var message = await _bot.SendTextMessageAsync(roomId, finalText, ParseMode.Html, false, false, 0,
                        new InlineKeyboardMarkup(buttons)
                    );
                    await Task.Delay(100);
                    await _bot.PinMessageIfAllowed(message, CancellationToken.None);
                    telegramVote.SetMessageId(message.MessageId);
                }
                else
                {
                    await _bot.EditMessageTextAsync(roomId, messageId.Value, finalText, ParseMode.Html, false,
                        new InlineKeyboardMarkup(buttons));
                }
            });
        }
    }
}