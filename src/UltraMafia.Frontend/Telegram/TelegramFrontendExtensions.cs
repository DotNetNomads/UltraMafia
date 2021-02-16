using System;
using System.Collections.Concurrent;
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
using UltraMafia.Frontend.Model;
using UltraMafia.Frontend.Model.Config;

namespace UltraMafia.Frontend.Telegram
{
    public static class TelegramFrontendExtensions
    {
        private static readonly ConcurrentDictionary<int, RegistrationMessageInfo>
            RegistrationMessageRegistry =
                new();

        private static readonly ConcurrentDictionary<int, (string actionName, int gamerId)> ActionsRegistry =
            new();

        private static readonly ConcurrentDictionary<string, string?> LastWordsRegistry =
            new();

        private static readonly ConcurrentDictionary<string, TelegramVote> VoteRegistry =
            new();

        
        private static User _sBotUser;

        private static readonly ConcurrentDictionary<long, (DateTime checkedAt, bool isAllowed)> PinAllowedRegistry =
            new ConcurrentDictionary<long, (DateTime checkedAt, bool allowed)>();

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
            if (!RegistrationMessageRegistry.TryRemove(session.Id, out var messageInfo))
                return;
            messageInfo.CancellationTokenSource.Cancel(false);
            if (messageInfo.CurrentMessageId.HasValue)
                await bot.LockAndDo(() =>
                    bot.DeleteMessageAsync(session.Room.ExternalRoomId, messageInfo.CurrentMessageId.Value));
        }

        public static async Task CreateRegistrationMessage(this ITelegramBotClient bot, GameSession newSession,
            TelegramFrontendSettings settings)
        {
            var registrationMessageInfo = new RegistrationMessageInfo
            {
                CancellationTokenSource = new CancellationTokenSource(),
                Session = newSession
            };
            if (!RegistrationMessageRegistry.TryAdd(newSession.Id, registrationMessageInfo))
            {
                Log.Error("Can't add message info to registry. {0}", JsonConvert.SerializeObject(newSession));
                return;
            }

            while (true)
            {
                // check, that repeating is disabled!
                if (registrationMessageInfo.CancellationTokenSource.IsCancellationRequested)
                    break;
                try
                {
                    // deleting old message if exist
                    if (registrationMessageInfo.CurrentMessageId.HasValue)
                    {
                        await bot.LockAndDo(async () =>
                        {
                            await bot.DeleteMessageAsync(new ChatId(newSession.Room.ExternalRoomId),
                                registrationMessageInfo.CurrentMessageId.Value,
                                registrationMessageInfo.CancellationTokenSource.Token);
                        });
                        await Task.Delay(1000, registrationMessageInfo.CancellationTokenSource.Token);
                    }

                    var text = GenerateRegistrationMessage(registrationMessageInfo.Session, settings, out var buttons);

                    Message? message = null;
                    await bot.LockAndDo(async () =>
                    {
                        message = await bot.SendTextMessageAsync(newSession.Room.ExternalRoomId, text,
                            ParseMode.Html,
                            false,
                            false, 0,
                            new InlineKeyboardMarkup(buttons), registrationMessageInfo.CancellationTokenSource.Token);
                    });
                    if (message == null)
                    {
                        Log.Error(
                            $"Error occured when bot tried send registration message to room: {newSession.Room.Id}");
                        break;
                    }

                    registrationMessageInfo.CurrentMessageId = message.MessageId;
                    await Task.Delay(90000, registrationMessageInfo.CancellationTokenSource.Token);
                }
                catch (TaskCanceledException)
                {
                    Log.Logger.Debug("Registration message was canceled.");
                    break;
                }
                catch (Exception ex)
                {
                    Log.Error("Error occured when bot tried send registration message", ex);
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

            if (currentMessageInfo.CancellationTokenSource.IsCancellationRequested)
                return;

            // update session in cache, for repeatable messages
            currentMessageInfo.Session = session;

            if (!currentMessageInfo.CurrentMessageId.HasValue)
                return;

            var text = GenerateRegistrationMessage(session, settings, out var buttons);

            await bot.LockAndDo(() => bot.EditMessageTextAsync(new ChatId(session.Room.ExternalRoomId),
                currentMessageInfo.CurrentMessageId.Value,
                text,
                ParseMode.Html, false,
                new InlineKeyboardMarkup(buttons), currentMessageInfo.CancellationTokenSource.Token));
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


        

        private static async Task<User> GetBotUser(this ITelegramBotClient bot, CancellationToken token)
        {
            if (_sBotUser != null)
                return _sBotUser;
            return _sBotUser = await bot.GetMeAsync(token);
        }

        private static async Task<bool> CheckPinIsAllowed(this ITelegramBotClient bot, long chatId,
            CancellationToken token)
        {
            // we are caching pin allowance only for one minute.
            if (PinAllowedRegistry.TryGetValue(chatId, out var info) &&
                (DateTime.Now - info.checkedAt).TotalSeconds <= 60)
                return info.isAllowed;
            // removing outdated information about pin permission
            var botUser = await bot.GetBotUser(token);
            var chatMember = await bot.GetChatMemberAsync(chatId, botUser.Id, token);
            var pinAllowed = chatMember.CanPinMessages ?? false;
            var newValue = (DateTime.Now, pinAllowed);
            PinAllowedRegistry.AddOrUpdate(chatId, newValue, (key, value) => newValue);
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
        public static void DeleteVote(string roomId) => VoteRegistry.TryRemove(roomId, out _);

        public static void AddVote(string roomId, TelegramVote vote) =>
            VoteRegistry.AddOrUpdate(roomId, vote, (key, _) => vote);

        public static TelegramVote? GetVoteInfo(string roomId) =>
            VoteRegistry.TryGetValue(roomId, out var voteInfo) ? voteInfo : null;

        # endregion

        #region LastWords

        public static bool IsLastWordsActual(string chatId) => LastWordsRegistry.ContainsKey(chatId);

        public static void SaveLastWords(string chatId, string lastWords) =>
            LastWordsRegistry.AddOrUpdate(chatId, lastWords, (key, _) => lastWords);

        public static void AllowLastWords(string chatId) => LastWordsRegistry.TryAdd(chatId, null);

        public static bool IsLastWordsWritten(string chatId) =>
            LastWordsRegistry.TryGetValue(chatId, out var lastWords) && lastWords != null;

        public static string? GetLastWords(string chatId) =>
            LastWordsRegistry.TryGetValue(chatId, out var lastWords) ? lastWords : null;

        public static void DisallowLastWords(string chatId) => LastWordsRegistry.TryRemove(chatId, out _);

        #endregion

        # region Actions

        public static bool IsActionProvided(int gameMemberId) => ActionsRegistry.ContainsKey(gameMemberId);

        public static (string actionName, int gamerId)? GetAction(int gameMemberId) =>
            ActionsRegistry.TryGetValue(gameMemberId, out var action)
                ? action
                : ((string actionName, int gamerId)?) null;

        public static void RemoveAction(int gameMemberId) => ActionsRegistry.TryRemove(gameMemberId, out _);

        public static void SaveAction(int actionFromId, (string actionName, int gamerId) actionInfo) =>
            ActionsRegistry.AddOrUpdate(actionFromId, actionInfo, (key, _) => actionInfo);

        #endregion
    }
}