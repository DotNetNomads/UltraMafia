using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using UltraMafia.DAL;
using UltraMafia.DAL.Enums;
using UltraMafia.DAL.Model;
using UltraMafia.GameModel;
using UltraMafia.Helpers;
using static UltraMafia.DAL.Enums.GameActions;

namespace UltraMafia.Frontend.Telegram
{
    public class TelegramFrontend : IFrontend
    {
        private DateTime _startTime;
        private readonly GameDbContext _dataContext;
        private readonly TelegramFrontendSettings _settings;
        private TelegramBotClient _bot;

        private readonly List<(int sessionId, int messageId)> _registrationMessageRegistry =
            new List<(int sessionId, int messageId)>();

        private readonly Dictionary<int, (string actionName, int gamerId)> _actionsRegistry =
            new Dictionary<int, (string actionName, int gamerId)>();

        private readonly ConcurrentDictionary<string, string?> _lastWordsRegistry =
            new ConcurrentDictionary<string, string>();

        private readonly ConcurrentDictionary<string, TelegramVote> _voteRegistry =
            new ConcurrentDictionary<string, TelegramVote>();

        private readonly SemaphoreSlim _botLock = new SemaphoreSlim(1);


        public TelegramFrontend(GameDbContext dataContext, TelegramFrontendSettings settings)
        {
            _dataContext = dataContext;
            _settings = settings;
        }

        #region Internal Helpers

        private async Task<GamerAccount> ResolveGamerAccount(Message message)
        {
            var user = message.From;
            var userId = user.Id;
            var userChatId = message.Chat.Type == ChatType.Private ? message.Chat.Id : 0;
            var nickName = user switch
            {
                _ when user.FirstName != null && user.LastName != null => $"{user.FirstName} {user.LastName}",
                _ when user.FirstName != null => $"{user.FirstName}",
                _ when user.LastName != null => $"{user.LastName}",
                _ => $"{user.Username}"
            };
            var gamerAccount = await _dataContext.GamerAccounts.FirstOrDefaultAsync(g =>
                g.IdExternal == userId.ToString());
            if (gamerAccount != null)
            {
                if (nickName == gamerAccount.NickName && userChatId.ToString() == gamerAccount.PersonalRoomId)
                    return gamerAccount;
                gamerAccount.NickName = nickName;
                gamerAccount.PersonalRoomId = userChatId.ToString();
                await _dataContext.SaveChangesAsync();

                return gamerAccount;
            }

            gamerAccount = new GamerAccount
            {
                IdExternal = userId.ToString(),
                PersonalRoomId = userChatId.ToString(),
                NickName = nickName
            };
            await _dataContext.GamerAccounts.AddAsync(gamerAccount);
            await _dataContext.SaveChangesAsync();

            return gamerAccount;
        }

        private async Task RemoveRegistrationMessage(GameSession session)
        {
            var messageInfo = _registrationMessageRegistry.First(m =>
                m.sessionId == session.Id);
            try
            {
                await _botLock.WaitAsync();
                await Task.Delay(500);
                await _bot.DeleteMessageAsync(session.Room.ExternalRoomId, messageInfo.messageId);
                _registrationMessageRegistry.Remove(messageInfo);
            }
            catch (Exception e)
            {
                Console.WriteLine("Remove reg message");
                Console.WriteLine(e);
            }
            finally
            {
                _botLock.Release();
            }
        }

        private async Task<GameRoom> ResolveGameRoom(Message message)
        {
            // we're going to find the room for game, if it isn't exist, we should create it
            var room = await _dataContext.GameRooms.FirstOrDefaultAsync(r =>
                r.ExternalRoomId == message.Chat.Id.ToString());
            if (room != null) return room;
            room = new GameRoom
            {
                RoomName = message.Chat.Title,
                ExternalRoomId = message.Chat.Id.ToString()
            };
            await _dataContext.AddAsync(room);
            await _dataContext.SaveChangesAsync();

            return room;
        }

        private static void EnsurePublicChat(Message message)
        {
            if (message.Chat.Type != ChatType.Private && message.Chat.Type != ChatType.Channel)
                return;
            throw new InvalidOperationException("Это действие разрешено вызывать только из публичных чатов");
        }

        private async Task CreateOrUpdateRegistrationMessage(GameSession session)
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

            var buttons = new List<InlineKeyboardButton>()
            {
                new InlineKeyboardButton
                {
                    Text = "Я в деле! 🎮",
                    Url = $"https://t.me/{_settings.BotUserName}?start={session.RoomId}"
                }
            };
            var currentMessageId = _registrationMessageRegistry.FirstOrDefault(s =>
                s.sessionId == session.Id).messageId;
            try
            {
                await _botLock.WaitAsync();
                await Task.Delay(500);
                if (currentMessageId == default)
                {
                    var message = await _bot.SendTextMessageAsync(session.Room.ExternalRoomId, text, ParseMode.Html,
                        false,
                        false, 0,
                        new InlineKeyboardMarkup(buttons));
                    await _bot.PinChatMessageAsync(session.Room.ExternalRoomId, message.MessageId);
                    _registrationMessageRegistry.Add((session.Id, message.MessageId));
                }
                else
                {
                    await _bot.EditMessageTextAsync(new ChatId(session.Room.ExternalRoomId), currentMessageId, text,
                        ParseMode.Html, false,
                        new InlineKeyboardMarkup(buttons));
                }
            }
            catch (Exception e)
            {
                Console.WriteLine(e);
            }
            finally
            {
                _botLock.Release();
            }
        }

        #endregion

        #region Bot Handlers

        private async void BotOnOnUpdate(object sender, UpdateEventArgs e)
        {
            var update = e.Update;
            var handler = update.Type switch
            {
                UpdateType.Message => BotOnMessageReceived(update.Message),
                UpdateType.CallbackQuery => BotOnCallbackQueryReceived(update.CallbackQuery),
                _ => UnknownUpdateHandlerAsync(update)
            };

            try
            {
                await handler;
            }
            catch (Exception exception)
            {
                var errorMessage = exception switch
                {
                    ApiRequestException apiRequestException =>
                    $"Telegram API Error:\n[{apiRequestException.ErrorCode}]\n{apiRequestException.Message}",
                    _ => exception.ToString()
                };

                Console.WriteLine(errorMessage);
            }
        }

        private async Task UnknownUpdateHandlerAsync(Update update)
        {
            throw new NotImplementedException();
        }

        private async Task BotOnCallbackQueryReceived(CallbackQuery callBackQuery)
        {
            var data = callBackQuery.Data;
            Console.WriteLine($"Action: {callBackQuery.From.Username} => {data}");
            if (string.IsNullOrWhiteSpace(data))
                return;

            var requestArgs = data.Split("-");
            var actionName = requestArgs[0];

            if (string.IsNullOrWhiteSpace(actionName))
                return;

            if (actionName == "vote")
            {
                if (requestArgs.Length < 2)
                    return;
                EnsurePublicChat(callBackQuery.Message);
                var roomId = callBackQuery.Message.Chat.Id.ToString();
                var userId = callBackQuery.From.Id.ToString();
                var voice = requestArgs[1];
                var answer = await ProcessVoteAnswer(roomId, userId, voice);
                try
                {
                    await _botLock.WaitAsync();
                    await Task.Delay(500);
                    await _bot.AnswerCallbackQueryAsync(
                        callBackQuery.Id,
                        answer
                    );
                }
                catch (Exception e)
                {
                    Console.WriteLine(e);
                }
                finally
                {
                    _botLock.Release();
                }

                return;
            }

            // parsing args
            if (requestArgs.Length < 2)
                return;
            if (!int.TryParse(requestArgs[1], out var actionFromId))
                return;
            var parsedActionToId = 0;
            if (requestArgs.Length > 2 && !int.TryParse(requestArgs[2], out parsedActionToId))
                return;
            var actionToId = parsedActionToId > 0 ? parsedActionToId : (int?) null;

            var actionDoneText = "Действие выбрано!";
            try
            {
                _actionsRegistry.Add(actionFromId, (actionName, actionToId.Value));
            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                actionDoneText = $"Не удалось выбрать действие. {ex.Message}";
            }

            await _bot.AnswerCallbackQueryAsync(
                callBackQuery.Id,
                actionDoneText
            );
            var message = callBackQuery.Message;
            await _bot.EditMessageTextAsync(message.Chat.Id, message.MessageId, actionDoneText);
        }

        private async Task<string> ProcessVoteAnswer(string roomId, string userId, string voice)
        {
            if (!_voteRegistry.ContainsKey(roomId))
            {
                return "Действие не актуально!";
            }

            var voteInfo = _voteRegistry[roomId];

            // check allowance
            if (!voteInfo.AllowedToPassVoteUsersIds.ContainsKey(userId))
                return "Действие запрещено!";
            if (voteInfo.VoteAllowedPredicate != null && !voteInfo.VoteAllowedPredicate.Invoke((userId, voice)))
            {
                return "Голос отклонен";
            }

            voteInfo.AddOrUpdateVote(userId, voice);
            await UpdateVote(roomId, voteInfo);
            return "Голос принят";
        }

        private async Task BotOnMessageReceived(Message message)
        {
            // skip old messages
            if (message.Date < _startTime || message.Type != MessageType.Text)
                return;

            // trying to parse bot commands
            var text = message.Text;

            try
            {
                var action = text switch
                {
                    _ when text.StartsWith("/start") => ProcessStartCommand(message),
                    _ when text.StartsWith("/go") => ProcessGoCommand(message),
                    _ when text.StartsWith("/game") => ProcessGameCommand(message),
                    _ when text.StartsWith("/stop") => ProcessStopCommand(message),
                    _ when text.StartsWith("/leave") => ProcessLeaveCommand(message),
                    _ => ProcessMessageDefault(message)
                };
                var result = await action;
                if (result == null)
                    return;
                await _bot.SendTextMessageAsync(message.Chat.Id, result, ParseMode.Default, false, false,
                    message.MessageId);
            }
            catch (Exception e)
            {
                await _bot.SendTextMessageAsync(message.Chat.Id, e.Message, ParseMode.Default, false, false,
                    message.MessageId);
            }
        }

        #endregion

        #region Actions

        private async Task<string> ProcessJoinToGame(int roomId, Message joinMessage)
        {
            var room = await _dataContext.GameRooms.FirstOrDefaultAsync(r => r.Id == roomId)
                       ?? throw new Exception("Игры не существует!");

            var gamerAccount = await ResolveGamerAccount(joinMessage);

            var gameSession = await RegistrationHandler.Invoke(room, gamerAccount);

            if (gameSession.GameMembers.Count == 4)
            {
                await _bot.SendTextMessageAsync(gameSession.Room.ExternalRoomId,
                    "Четыре игрока есть, можно начинать игру. Но, чем больше игроков, тем лучше.");
            }

            await CreateOrUpdateRegistrationMessage(gameSession);
            return "Ты в игре :)";
        }

        private async Task<string> ProcessLeaveCommand(Message message)
        {
            // 1. should check this message from public chat
            // 2. resolve room by massage.Chat.Id
            // 3. resolve gamer by message.From.UserId
            // 4. Call GameService LeaveGameHandler (room, gamer);
            // 5. If it returns nothing, it's ok. otherwise return error.
            return "Good bye";
        }

        private async Task<string> ProcessStopCommand(Message message)
        {
            EnsurePublicChat(message);
            var room = await ResolveGameRoom(message);
            var gamerAccount = await ResolveGamerAccount(message);
            var session = await StopGameHandler(room, gamerAccount);
            await RemoveRegistrationMessage(session);
            await _bot.DeleteMessageAsync(message.Chat.Id, message.MessageId);
            await _bot.SendTextMessageAsync(message.Chat.Id, "Регистрация в игру остановлена!");
            return null;
        }

        private async Task<string> ProcessGameCommand(Message message)
        {
            EnsurePublicChat(message);
            var room = await ResolveGameRoom(message);
            var gamerAccount = await ResolveGamerAccount(message);
            var session = await GameCreationHandler(room, gamerAccount);
            await CreateOrUpdateRegistrationMessage(session);
            await _bot.DeleteMessageAsync(message.Chat.Id, message.MessageId);
            return null;
        }

        private async Task<string> ProcessGoCommand(Message message)
        {
            var gameRoom = await ResolveGameRoom(message);
            var session = await GameStartHandler(gameRoom);
            await RemoveRegistrationMessage(session);
            return null;
        }

        private Task<string> ProcessStartCommand(Message message)
        {
            if (!int.TryParse(message.Text.Substring(6), out var roomId))
                throw new Exception(
                    "Невозможно зарегистрировать в игре. Отсутствует номер игровой комнаты. Сначала нажмите на кнопку в общем чате.");

            return ProcessJoinToGame(roomId, message);
        }

        private async Task<string> ProcessMessageDefault(Message message)
        {
            var chatId = message.Chat.Id.ToString();
            var isPersonal = message.Chat.Type == ChatType.Private;
            // mb last words?
            if (isPersonal && _lastWordsRegistry.ContainsKey(chatId))
                _lastWordsRegistry.TryUpdate(chatId, message.Text, null);

            return null;
        }

        #endregion

        public void EnableGame()
        {
            _startTime = DateTime.UtcNow;
            _bot = new TelegramBotClient(_settings.Token);
            _bot.StartReceiving(new[]
            {
                UpdateType.CallbackQuery, UpdateType.Message
            });

            _bot.OnUpdate += BotOnOnUpdate;
        }

        public Task MuteAll(GameRoom room, DateTime until)
        {
            throw new NotImplementedException();
        }

        public Task UnmuteAll(GameRoom room)
        {
            throw new NotImplementedException();
        }

        public async Task SendMessageToGamer(GamerAccount gamer, string message)
        {
            if (gamer.PersonalRoomId == null || gamer.PersonalRoomId == "0")
                return;

            await _botLock.WaitAsync();
            try
            {
                await Task.Delay(500);
                await _bot.SendTextMessageAsync(gamer.PersonalRoomId, message, ParseMode.Html);
            }
            finally
            {
                _botLock.Release();
            }
        }

        public async Task SendMessageToRoom(GameRoom room, string message, bool important = false)
        {
            await _botLock.WaitAsync();
            try
            {
                await Task.Delay(500);
                var messageObj = await _bot.SendTextMessageAsync(room.ExternalRoomId, message, ParseMode.Html);
                if (important)
                {
                    await _bot.PinChatMessageAsync(room.ExternalRoomId, messageObj.MessageId);
                }
            }
            finally
            {
                _botLock.Release();
            }
        }

        public Task MuteSpecificGamers(GameRoom room, GamerAccount[] gamers)
        {
            throw new NotImplementedException();
        }

        public Task UnmuteSpecificGamers(GameRoom room, GamerAccount[] gamers)
        {
            throw new NotImplementedException();
        }

        public Func<(GameRoom room, GamerAccount gamerFrom, GameActions action, GamerAccount target), Task>
            ActionHandler { get; set; }

        public Func<GameRoom, GamerAccount, Task<GameSession>> RegistrationHandler { get; set; }
        public Func<GameRoom, GamerAccount, Task<GameSession>> GameCreationHandler { get; set; }
        public Func<GameRoom, GamerAccount, Task<GameSession>> StopGameHandler { get; set; }
        public Func<GameRoom, Task<GameSession>> GameStartHandler { get; set; }
        public Func<GamerAccount, string, Task> MessageHandler { get; set; }

        public Task<ActionDescriptor> AskDoctorForAction(GameSessionMember doctor,
            GameSessionMember[] membersToSelect) => AskForAction(
            "Уважаемый доктор. <b>Кого будем лечить?</b> 🚑",
            doctor,
            membersToSelect,
            (member, index) => new (GameActions?, string)[]
            {
                (Healing, $"{index}. {member.GamerAccount.NickName}")
            });

        public Task<ActionDescriptor> AskCopForAction(GameSessionMember? cop,
            GameSessionMember[] aliveMembers) => AskForAction(
            @"Уважаемый Коммисар. <b>Что будем делать этой ночью?</b> 👮
Доступные действия: 
  🔎 - проверить жителя
  ⚔️ - убить жителя
",
            cop,
            aliveMembers.Where(gm => gm.Role != GameRoles.Cop),
            (member, index) => new (GameActions?, string)[]
            {
                (null, $"{index}. {member.GamerAccount.NickName}"),
                (Checkup, "🔎"),
                (Killing, "⚔️")
            }
        );

        public async Task<string?> GetLastWords(GamerAccount gamerAccount)
        {
            var gamerChatId = gamerAccount.PersonalRoomId;
            _lastWordsRegistry.TryAdd(gamerChatId, null);

            const int maxTries = 12;
            // we waiting for last words (one minute)
            for (var currentTry = 1;
                currentTry <= maxTries;
                currentTry++)
            {
                await Task.Delay(3000);
                if (_lastWordsRegistry[gamerChatId] != null)
                    break;
            }

            _lastWordsRegistry.TryRemove(gamerChatId, out var lastWords);
            return lastWords;
        }

        public Task<ActionDescriptor> AskMafiaForAction(GameSessionMember mafia, GameSessionMember[] availableGamers) =>
            AskForAction(
                @"Дорогой мафиози. <b>Что будем делать этой ночью?</b> 😈
Доступные действия: 
  🔎 - проверить жителя
  ⚔️ - убить жителя
",
                mafia,
                availableGamers.Where(gm => gm.Role != GameRoles.Mafia),
                (member, index) => new (GameActions?, string)[]
                {
                    (null, $"{index}. {member.GamerAccount.NickName}"),
                    (Checkup, "🔎"),
                    (Killing, "⚔️")
                }
            );

        public async Task<VoteDescriptor[]> CreateLynchVoteAndReceiveResults(GameRoom sessionRoom,
            GameSessionMember[] allowedMembers)
        {
            // creating vote
            var variants = allowedMembers.Select(m => (m.GamerAccount.NickName, m.GamerAccount.IdExternal)).ToArray();
            var allowedToVoteUserIds = new Dictionary<string, string>();
            foreach (var gameSessionMember in allowedMembers)
            {
                if (!allowedToVoteUserIds.ContainsKey(gameSessionMember.GamerAccount.IdExternal))
                {
                    allowedToVoteUserIds.Add(gameSessionMember.GamerAccount.IdExternal,
                        gameSessionMember.GamerAccount.NickName);
                }
            }

            var telegramVote = new TelegramVote(variants, "<i>Кого будем вешать сегодня?</i> 🎲", allowedToVoteUserIds,
                request =>
                    request.userId != request.voice);
            await CreateVote(telegramVote, sessionRoom.ExternalRoomId);
            const int maxTries = 6;
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

            return voices.Select(v => new VoteDescriptor
            {
                VoiceOwner = allowedMembers.First(g => g.GamerAccount.IdExternal == v.UserId),
                VoiceTarget = allowedMembers.First(g => g.GamerAccount.IdExternal == v.Voice)
            }).ToArray();
        }

        private async Task FinishVote(TelegramVote telegramVote, string roomId)
        {
            _voteRegistry.TryRemove(roomId, out _);
            await UpdateVote(roomId, telegramVote, true);
        }

        private async Task CreateVote(TelegramVote telegramVote, string roomId)
        {
            _voteRegistry.TryAdd(roomId, telegramVote);
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
                await _bot.EditMessageTextAsync(roomId, messageId.Value,
                    $"{finalText}\n<b>Голосование завершено!</b>", ParseMode.Html, false,
                    null);
                return;
            }

            var buttons = telegramVote
                .Variants
                .Select(variant => new List<InlineKeyboardButton>
                {
                    new InlineKeyboardButton
                    {
                        Text = $"{variant.uiName} ({voices.Count(v => v.Voice == variant.internalName)})",
                        CallbackData = $"vote-{variant.internalName}"
                    }
                })
                .ToArray();
            if (messageId == null)
            {
                var message = await _bot.SendTextMessageAsync(roomId, finalText, ParseMode.Html, false, false, 0,
                    new InlineKeyboardMarkup(buttons)
                );
                await _bot.PinChatMessageAsync(roomId, message.MessageId);
                telegramVote.SetMessageId(message.MessageId);
            }
            else
            {
                await _bot.EditMessageTextAsync(roomId, messageId.Value, finalText, ParseMode.Html, false,
                    new InlineKeyboardMarkup(buttons));
            }
        }

        public async Task<ApproveVoteDescriptor[]> CreateLynchApprovalVote(GameRoom sessionRoom,
            GameSessionMember[] allowedMembers,
            GameSessionMember lynchTarget)
        {
            // creating vote
            var variants = new[] {("👍", $"yes"), ("👎", "no")};
            var allowedToVoteUserIds = new Dictionary<string, string>();
            foreach (var gameSessionMember in allowedMembers)
            {
                if (!allowedToVoteUserIds.ContainsKey(gameSessionMember.GamerAccount.IdExternal))
                {
                    allowedToVoteUserIds.Add(gameSessionMember.GamerAccount.IdExternal,
                        gameSessionMember.GamerAccount.NickName);
                }
            }

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
                    "no" => false, "yes" => true,
                    // _ => throw new InvalidOperationException("Lynch answer is incorrect")
                    _ => false
                }
            }).ToArray();
        }

        private Task MarkActionAsOutdated(Message message) =>
            _bot.EditMessageTextAsync(message.Chat.Id, message.MessageId,
                "Поздно. Ты не успел нажать на кнопочки :(");

        private async Task<ActionDescriptor> AskForAction(string text,
            GameSessionMember actionFromMember,
            IEnumerable<GameSessionMember> availableMembers,
            Func<GameSessionMember, int, (GameActions? actionName, string uiText)[]> buttonsTemplateBuilder,
            int secondsToWaitForAnswer = 40)
        {
            var keyboard = availableMembers
                .SelectMany((member, index) =>
                {
                    var template = buttonsTemplateBuilder(member, index);
                    var result = new List<List<InlineKeyboardButton>>();
                    // detecting header button, if exists
                    var currentIndex = 0;
                    if (template[0] is {} header && header.actionName == null)
                    {
                        result.Add(new List<InlineKeyboardButton>()
                        {
                            new InlineKeyboardButton
                            {
                                Text = header.uiText,
                                CallbackData = " "
                            }
                        });
                        currentIndex++;
                    }

                    var actionButtons = new List<InlineKeyboardButton>();
                    while (currentIndex < template.Length)
                    {
                        var (actionName, uiText) = template[currentIndex];
                        actionButtons.Add(new InlineKeyboardButton
                        {
                            Text = uiText,
                            CallbackData = $"{actionName}-{actionFromMember.Id}-{member.Id}"
                        });
                        currentIndex++;
                    }

                    result.Add(actionButtons);
                    return result;
                }).ToList();

            Message message;
            try
            {
                await _botLock.WaitAsync();
                await Task.Delay(500);
                message = await _bot.SendTextMessageAsync(actionFromMember.GamerAccount.PersonalRoomId,
                    text,
                    ParseMode.Html, false, false, 0, new InlineKeyboardMarkup(keyboard));
            }
            catch (Exception ex)
            {
                Console.WriteLine(text);
                Console.WriteLine(ex);
                Console.WriteLine("Error occured, returning default result");
                return new ActionDescriptor();
            }
            finally
            {
                _botLock.Release();
            }

            // calculation how much tries with 5sec step
            var tries = secondsToWaitForAnswer / 5;
            GameSessionMember? target = null;

            GameActions? action = null;
            while (tries > 0)
            {
                // sleep and wait for user answer
                await Task.Delay(5000);
                // check answer
                if (_actionsRegistry.ContainsKey(actionFromMember.Id))
                {
                    var (actionName, gamerId) = _actionsRegistry[actionFromMember.Id];
                    _actionsRegistry.Remove(actionFromMember.Id);
                    target = availableMembers.FirstOrDefault(g => g.Id == gamerId);
                    action = Enum.Parse<GameActions>(actionName);
                    break;
                }

                // try again, and update counter
                tries--;
            }

            // delete action message, because it's outdated.
            if (target == null) await MarkActionAsOutdated(message);
            return new ActionDescriptor(action, target);
        }
    }
}