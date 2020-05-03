using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Serilog;
using Serilog.Core;
using Telegram.Bot;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using UltraMafia.DAL;
using UltraMafia.DAL.Model;
using UltraMafia.Helpers;

namespace UltraMafia.Frontend.Telegram
{
    public static class TelegramFrontendExtensions
    {
        private static readonly Dictionary<int, (int messageId, CancellationTokenSource repeatCancelerationToken)>
            RegistrationMessageRegistry =
                new Dictionary<int, (int messageId, CancellationTokenSource repeatCancellationToken)>();

        private static readonly Dictionary<int, (string actionName, int gamerId)> ActionsRegistry =
            new Dictionary<int, (string actionName, int gamerId)>();

        private static readonly Dictionary<string, string?> LastWordsRegistry =
            new Dictionary<string, string?>();

        private static readonly Dictionary<string, TelegramVote> VoteRegistry =
            new Dictionary<string, TelegramVote>();

        private static readonly SemaphoreSlim BotLock = new SemaphoreSlim(1);
        private static User? s_botUser;

        private static readonly Dictionary<long, (DateTime checkedAt, bool isAllowed)> PinAllowedRegistry =
            new Dictionary<long, (DateTime checkedAt, bool allowed)>();

        private static readonly Dictionary<int, GameSession> SessionCache = new Dictionary<int, GameSession>();

        #region DbContext

        public static async Task<GamerAccount> ResolveOrCreateGamerAccountFromTelegramMessage(
            this GameDbContext context, Message message)
        {
            var user = message.From;
            var userId = user.Id;
            var userChatId = message.Chat.Type == ChatType.Private ? message.Chat.Id.ToString() : "0";
            var nickName = user switch
            {
                _ when user.FirstName != null && user.LastName != null => $"{user.FirstName} {user.LastName}",
                _ when user.FirstName != null => $"{user.FirstName}",
                _ when user.LastName != null => $"{user.LastName}",
                _ => $"{user.Username}"
            };
            var gamerAccount = await context.GamerAccounts.FirstOrDefaultAsync(g =>
                g.IdExternal == userId.ToString());
            Log.Debug("Resolving gamer account for userId={0}, name={1}, chatId={2}", userId, nickName, userChatId);
            if (gamerAccount != null)
            {
                Log.Debug("Gamer account with ID={0} loaded from database.", gamerAccount.Id);
                if (nickName == gamerAccount.NickName &&
                    (userChatId == gamerAccount.PersonalRoomId || userChatId == "0"))
                    return gamerAccount;
                Log.Debug("Info updated nickName={0}=>{1} personalRoomId={2}=>{3}", gamerAccount.NickName, nickName,
                    gamerAccount.PersonalRoomId, userChatId);
                gamerAccount.NickName = nickName;
                gamerAccount.PersonalRoomId = userChatId;
                await context.SaveChangesAsync();

                return gamerAccount;
            }

            gamerAccount = new GamerAccount
            {
                IdExternal = userId.ToString(),
                PersonalRoomId = userChatId,
                NickName = nickName
            };
            Log.Debug("New gamer account created");
            await context.GamerAccounts.AddAsync(gamerAccount);
            await context.SaveChangesAsync();

            return gamerAccount;
        }

        public static async Task<GameRoom> ResolveOrCreateGameRoomFromTelegramMessage(this GameDbContext context,
            Message message)
        {
            // we're going to find the room for game, if it isn't exist, we should create it
            var room = await context.GameRooms.FirstOrDefaultAsync(r =>
                r.ExternalRoomId == message.Chat.Id.ToString());
            if (room != null) return room;
            room = new GameRoom
            {
                RoomName = message.Chat.Title,
                ExternalRoomId = message.Chat.Id.ToString()
            };
            await context.AddAsync(room);
            await context.SaveChangesAsync();

            return room;
        }

        #endregion

        #region Telegram Bot

        public static async Task RemoveRegistrationMessage(this ITelegramBotClient bot, GameSession session)
        {
            await bot.LockAndDo(async () =>
            {
                if (!RegistrationMessageRegistry.TryGetValue(session.Id, out var messageInfo))
                    return;
                // remove session from cache
                SessionCache.Remove(session.Id);
                messageInfo.repeatCancelerationToken.Cancel(false);
                await bot.DeleteMessageAsync(session.Room.ExternalRoomId, messageInfo.messageId);
                RegistrationMessageRegistry.Remove(session.Id);
            });
        }

        public static void EnsurePublicChat(this Message message)
        {
            if (message.Chat.Type != ChatType.Private && message.Chat.Type != ChatType.Channel)
                return;
            throw new InvalidOperationException("Это действие разрешено вызывать только из публичных чатов");
        }

        public static async Task CreateRegistrationMessage(this ITelegramBotClient bot, GameSession newSession,
            TelegramFrontendSettings settings)
        {
            var repeatCancellationTokenSource = new CancellationTokenSource();
            SessionCache.Add(newSession.Id, newSession);
            while (true)
            {
                // check, that repeating is disabled!
                if (repeatCancellationTokenSource.IsCancellationRequested)
                    break;
                try
                {
                    // deleting old message if exist
                    await bot.LockAndDo(async () =>
                    {
                        if (RegistrationMessageRegistry.TryGetValue(newSession.Id, out var lastMessageInfo))
                        {
                            await bot.DeleteMessageAsync(new ChatId(newSession.Room.ExternalRoomId),
                                lastMessageInfo.messageId,
                                lastMessageInfo.repeatCancelerationToken.Token);
                            RegistrationMessageRegistry.Remove(newSession.Id);
                        }
                    });

                    var text = GenerateRegistrationMessage(SessionCache[newSession.Id], settings, out var buttons);

                    Message? message = null;
                    await bot.LockAndDo(async () =>
                    {
                        message = await bot.SendTextMessageAsync(newSession.Room.ExternalRoomId, text,
                            ParseMode.Html,
                            false,
                            false, 0,
                            new InlineKeyboardMarkup(buttons), repeatCancellationTokenSource.Token);
                        await Task.Delay(100, repeatCancellationTokenSource.Token);
                        await bot.PinMessageIfAllowed(message, repeatCancellationTokenSource.Token);
                        RegistrationMessageRegistry.Add(newSession.Id,
                            (message.MessageId, repeatCancellationTokenSource));
                    });
                    if (message == null)
                    {
                        Log.Error(
                            $"Error occured when bot tried send registration message to room: {newSession.Room.Id}");
                        break;
                    }

                    await Task.Delay(30000, repeatCancellationTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error("Error occured when bot tried send registration message");
                }
            }
        }

        public static async Task UpdateRegistrationMessage(this ITelegramBotClient bot, GameSession session,
            TelegramFrontendSettings settings)
        {
            if (!RegistrationMessageRegistry.TryGetValue(session.Id, out var currentMessageInfo))
            {
                await CreateRegistrationMessage(bot, session, settings);
                return;
            }

            if (currentMessageInfo.repeatCancelerationToken.IsCancellationRequested)
                return;

            // update session in cache, for repeatable messages
            SessionCache[session.Id] = session;

            var text = GenerateRegistrationMessage(session, settings, out var buttons);

            await bot.LockAndDo(() => bot.EditMessageTextAsync(new ChatId(session.Room.ExternalRoomId),
                currentMessageInfo.messageId,
                text,
                ParseMode.Html, false,
                new InlineKeyboardMarkup(buttons), currentMessageInfo.repeatCancelerationToken.Token));
        }

        private static string GenerateRegistrationMessage(GameSession session, TelegramFrontendSettings settings,
            out List<InlineKeyboardButton> buttons)
        {
            var text =
                $"<b>Создатель игры: <i>{session.CreatedByGamerAccount.NickName}</i></b>\n\n<b>Набор игроков</b> \n\n";

            if (session.GameMembers.Any())
            {
                text += "Игроки:  \n";
            }

            var index = 1;
            foreach (var member in session.GameMembers)
            {
                text += $"{index}. {member.GamerAccount.NickName} \n";
                index++;
            }

            buttons = new List<InlineKeyboardButton>()
            {
                new InlineKeyboardButton
                {
                    Text = "Я в деле! 🎮",
                    Url = $"https://t.me/{settings.BotUserName}?start={session.RoomId}"
                }
            };
            return text;
        }


        public static async Task LockAndDo(this ITelegramBotClient bot, Func<Task> action)
        {
            try
            {
                await BotLock.WaitAsync();
                await Task.Delay(50);
                await action();
            }
            catch (Exception e)
            {
                Log.Error(e, "Error occured when accessing to Bot API");
            }

            finally
            {
                BotLock.Release();
            }
        }

        private static async Task<User> GetBotUser(this ITelegramBotClient bot, CancellationToken token)
        {
            if (s_botUser != null)
                return s_botUser;
            return s_botUser = await bot.GetMeAsync(token);
        }

        private static async Task<bool> CheckPinIsAllowed(this ITelegramBotClient bot, long chatId,
            CancellationToken token)
        {
            // we are caching pin allowance only for one minute.
            if (PinAllowedRegistry.TryGetValue(chatId, out var info) &&
                (DateTime.Now - info.checkedAt).TotalSeconds <= 60)
                return info.isAllowed;
            // removing outdated information about pin permission
            if (PinAllowedRegistry.ContainsKey(chatId)) PinAllowedRegistry.Remove(chatId);
            var botUser = await bot.GetBotUser(token);
            var chatMember = await bot.GetChatMemberAsync(chatId, botUser.Id, token);
            var pinAllowed = chatMember.CanPinMessages ?? false;
            PinAllowedRegistry.Add(chatId, (DateTime.Now, pinAllowed));
            return pinAllowed;
        }

        public static async Task PinMessageIfAllowed(this ITelegramBotClient bot, Message message,
            CancellationToken token)
        {
            if (await bot.CheckPinIsAllowed(message.Chat.Id, token))
            {
                try
                {
                    await bot.PinChatMessageAsync(message.Chat.Id, message.MessageId, false, token);
                }
                catch (Exception e)
                {
                    Log.Error("Error occured when bot tried to pin message: {0}", e,
                        JsonConvert.SerializeObject(message));
                }
            }
        }

        #endregion

        # region Votes

        public static bool IsActiveVote(string roomId) => VoteRegistry.ContainsKey(roomId);
        public static void DeleteVote(string roomId) => VoteRegistry.Remove(roomId);
        public static void AddVote(string roomId, TelegramVote vote) => VoteRegistry.Add(roomId, vote);
        public static TelegramVote GetVoteInfo(string roomId) => VoteRegistry[roomId];

        # endregion

        #region LastWords

        public static bool IsLastWordsActual(string chatId) => LastWordsRegistry.ContainsKey(chatId);
        public static void SaveLastWords(string chatId, string lastWords) => LastWordsRegistry[chatId] = lastWords;
        public static void AllowLastWords(string chatId) => LastWordsRegistry.Add(chatId, null);
        public static bool IsLastWordsWritten(string chatId) => LastWordsRegistry[chatId] != null;
        public static string GetLastWords(string chatId) => LastWordsRegistry[chatId];
        public static void DisallowLastWords(string chatId) => LastWordsRegistry.Remove(chatId);

        #endregion

        # region Actions

        public static bool IsActionProvided(int gameMemberId) => ActionsRegistry.ContainsKey(gameMemberId);
        public static (string actionName, int gamerId) GetAction(int gameMemberId) => ActionsRegistry[gameMemberId];
        public static void RemoveAction(int gameMemberId) => ActionsRegistry.Remove(gameMemberId);

        public static void SaveAction(int actionFromId, (string actionName, int gamerId) actionInfo)
        {
            if (ActionsRegistry.ContainsKey(actionFromId))
            {
                throw new Exception("Action already selected!");
            }

            ActionsRegistry.Add(actionFromId, actionInfo);
        }

        #endregion
    }
}