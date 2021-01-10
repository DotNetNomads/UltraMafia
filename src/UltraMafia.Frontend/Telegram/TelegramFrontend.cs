using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using UltraMafia.Common.Config;
using UltraMafia.Common.GameModel;
using UltraMafia.DAL.Enums;
using UltraMafia.DAL.Extensions;
using UltraMafia.DAL.Model;
using UltraMafia.Frontend.Telegram.Config;
using static UltraMafia.DAL.Enums.GameActions;

namespace UltraMafia.Frontend.Telegram
{
    public class TelegramFrontend : IFrontend
    {
        private DateTime _startTime;
        private readonly TelegramFrontendSettings _settings;
        private readonly GameSettings _gameSettings;
        private readonly TelegramBotClient _bot;
        private readonly IServiceProvider _serviceProvider;


        public TelegramFrontend(TelegramFrontendSettings settings, IServiceProvider serviceProvider,
            GameSettings gameSettings)
        {
            _settings = settings;
            _serviceProvider = serviceProvider;
            _gameSettings = gameSettings;
            _bot = new TelegramBotClient(_settings.Token);
        }

        #region Bot Handlers

        private async void BotOnOnUpdate(object sender, UpdateEventArgs e)
        {
            var update = e.Update;
            var handler = update.Type switch
            {
                UpdateType.Message => BotOnMessageReceived(update.Message),
                UpdateType.CallbackQuery => BotOnCallbackQueryReceived(update.CallbackQuery),
                _ => null
            };

            try
            {
                if (handler == null)
                    return;

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

                Log.Error(exception, errorMessage);
            }
        }

        private async Task BotOnCallbackQueryReceived(CallbackQuery callBackQuery)
        {
            var data = callBackQuery.Data;
            Log.Debug("User {0} doing action {1}", callBackQuery.From.Id, data);
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
                callBackQuery.Message.EnsurePublicChat();
                var roomId = callBackQuery.Message.Chat.Id.ToString();
                var userId = callBackQuery.From.Id.ToString();
                var voice = requestArgs[1];
                var answer = await ProcessVoteAnswer(roomId, userId, voice);
                await _bot.LockAndDo(() => _bot.AnswerCallbackQueryAsync(
                    callBackQuery.Id,
                    answer
                ));
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
                TelegramFrontendExtensions.SaveAction(actionFromId, (actionName, actionToId.Value));
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error occured when user tried to give a voice");
                actionDoneText = $"Не удалось выбрать действие. {ex.Message}";
            }

            var message = callBackQuery.Message;
            await _bot.LockAndDo(async () =>
            {
                await _bot.AnswerCallbackQueryAsync(
                    callBackQuery.Id,
                    actionDoneText
                );
                await Task.Delay(100);
                await _bot.EditMessageTextAsync(message.Chat.Id, message.MessageId, actionDoneText);
            });
        }

        private async Task<string> ProcessVoteAnswer(string roomId, string userId, string voice)
        {
            if (!TelegramFrontendExtensions.IsActiveVote(roomId))
            {
                return "Действие не актуально!";
            }

            var voteInfo = TelegramFrontendExtensions.GetVoteInfo(roomId);
            if (voteInfo == null)
                return "Голосование не найдено";

            // check allowance
            if (!voteInfo.AllowedToPassVoteUsersIds.ContainsKey(userId))
                return "Действие запрещено!";
            if (!_gameSettings.DevelopmentMode && voteInfo.VoteAllowedPredicate != null &&
                !voteInfo.VoteAllowedPredicate.Invoke((userId, voice)))
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

            bool CommandMatch(string name) => text == $"/{name}" || text == $"/{name}@{_settings.BotUserName}";
            try
            {
                if (text.StartsWith("/start"))
                    await ProcessJoinCommand(message);
                else if (CommandMatch("go"))
                    await ProcessGoCommand(message);
                else if (CommandMatch("game"))
                    await ProcessGameCommand(message);
                else if (CommandMatch("stop"))
                    await ProcessStopCommand(message);
                else if (CommandMatch("leave"))
                    await ProcessLeaveCommand(message);
                else ProcessMessageDefault(message);
            }
            catch (Exception e)
            {
                await _bot.SendTextMessageAsync(message.Chat.Id, e.Message, ParseMode.Default, false, false,
                    message.MessageId);
                Log.Error(e, "Error occured in message handling");
            }
        }

        #endregion

        #region Actions

        private async Task ProcessLeaveCommand(Message message)
        {
            message.EnsurePublicChat();
            int roomId;
            int gamerAccountId;
            using (var dbContextAccessor = _serviceProvider.GetDbContext())
            {
                var room = await dbContextAccessor.DbContext.ResolveOrCreateGameRoomFromTelegramMessage(message);
                roomId = room.Id;
                var gamerAccount =
                    await dbContextAccessor.DbContext.ResolveOrCreateGamerAccountFromTelegramMessage(message);
                gamerAccountId = gamerAccount.Id;
            }

            OnGameLeaveRequest(roomId, gamerAccountId);
        }

        private async Task ProcessStopCommand(Message message)
        {
            message.EnsurePublicChat();
            int roomId;
            int gamerAccountId;
            using (var dbContextAccessor = _serviceProvider.GetDbContext())
            {
                var room = await dbContextAccessor.DbContext.ResolveOrCreateGameRoomFromTelegramMessage(message);
                roomId = room.Id;
                var gamerAccount =
                    await dbContextAccessor.DbContext.ResolveOrCreateGamerAccountFromTelegramMessage(message);
                gamerAccountId = gamerAccount.Id;
            }

            OnGameStopRequest(roomId, gamerAccountId);
        }

        private async Task ProcessGameCommand(Message message)
        {
            message.EnsurePublicChat();
            int roomId;
            int gamerAccountId;
            using (var dbContextAccessor = _serviceProvider.GetDbContext())
            {
                var room = await dbContextAccessor.DbContext.ResolveOrCreateGameRoomFromTelegramMessage(message);
                roomId = room.Id;
                var gamerAccount =
                    await dbContextAccessor.DbContext.ResolveOrCreateGamerAccountFromTelegramMessage(message);
                gamerAccountId = gamerAccount.Id;
            }

            OnGameCreationRequest(roomId, gamerAccountId);
        }

        private async Task ProcessGoCommand(Message message)
        {
            Log.Debug($"Processing go command from {message.From.Id}");
            int roomId;
            using (var dbContextAccessor = _serviceProvider.GetDbContext())
            {
                var room = await dbContextAccessor.DbContext.ResolveOrCreateGameRoomFromTelegramMessage(message);
                roomId = room.Id;
            }

            OnGameStartRequest(roomId);
        }

        private async Task ProcessJoinCommand(Message message)
        {
            Log.Debug($"Processing join command from {message.From.Id}");
            if (message.Chat.Type != ChatType.Private)
                return;
            var messageSplit = message.Text.Split(' ');
            if (messageSplit.Length < 2 || !int.TryParse(messageSplit[1], out var roomId))
                throw new Exception(
                    "Невозможно зарегистрировать в игре. Отсутствует номер игровой комнаты. Сначала нажмите на кнопку в общем чате.");

            int gamerAccountId;
            using (var dbContextAccessor = _serviceProvider.GetDbContext())
            {
                if (!await dbContextAccessor.DbContext.GameRooms.AnyAsync(r => r.Id == roomId))
                    throw new Exception("Игры не существует!");

                var gamerAccount =
                    await dbContextAccessor.DbContext.ResolveOrCreateGamerAccountFromTelegramMessage(message);
                gamerAccountId = gamerAccount.Id;
            }


            OnGameJoinRequest(roomId, gamerAccountId);
        }

        private void ProcessMessageDefault(Message message)
        {
            var chatId = message.Chat.Id.ToString();
            var isPersonal = message.Chat.Type == ChatType.Private;
            // mb last words?
            if (isPersonal && TelegramFrontendExtensions.IsLastWordsActual(chatId))
                TelegramFrontendExtensions.SaveLastWords(chatId, message.Text);
        }

        #endregion

        public void ActivateFrontend()
        {
            _startTime = DateTime.UtcNow;
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

        public Task MuteSpecificGamers(GameRoom room, GamerAccount[] gamers)
        {
            throw new NotImplementedException();
        }

        public Task UnmuteSpecificGamers(GameRoom room, GamerAccount[] gamers)
        {
            throw new NotImplementedException();
        }

        public Task<ActionDescriptor> AskDoctorForAction(GameSessionMember doctor,
            GameSessionMember[] membersToSelect) => AskForAction(
            "Уважаемый доктор. <b>Кого будем лечить?</b> 🚑 \n\n * - себя можно лечить только 1 раз за игру.",
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
            TelegramFrontendExtensions.AllowLastWords(gamerChatId);

            const int maxTries = 10;
            // we waiting for last words (one minute)
            for (var currentTry = 1;
                currentTry <= maxTries;
                currentTry++)
            {
                await Task.Delay(5000);
                if (TelegramFrontendExtensions.IsLastWordsWritten(gamerChatId))
                    break;
            }

            var lastWords = TelegramFrontendExtensions.GetLastWords(gamerChatId);
            TelegramFrontendExtensions.DisallowLastWords(gamerChatId);
            return lastWords;
        }

        public Task<ActionDescriptor> AskMafiaForAction(GameSessionMember mafia, GameSessionMember[] availableGamers)
        {
            var actionTextBuilder = new StringBuilder(@"Дорогой мафиози. <b>Что будем делать этой ночью?</b> 😈
Доступные действия: 
  🔎 - проверить жителя
  ⚔️ - убить жителя
");
            var mafiaGamers = availableGamers.Where(m => m.Role == GameRoles.Mafia && m.Id != mafia.Id)
                .Select(m => m.GamerAccount.NickName)
                .ToArray();
            if (mafiaGamers.Length > 1) actionTextBuilder.AppendLine($"Cоюзники: {string.Join(", ", mafiaGamers)}");

            return AskForAction(
                actionTextBuilder.ToString(),
                mafia,
                availableGamers.Where(gm => gm.Role != GameRoles.Mafia),
                (member, index) => new (GameActions?, string)[]
                {
                    (null, $"{index}. {member.GamerAccount.NickName}"),
                    (Checkup, "🔎"),
                    (Killing, "⚔️")
                }
            );
        }

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
                    new InlineKeyboardButton
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
                    "no" => false,
                    "yes" => true,
                    // _ => throw new InvalidOperationException("Lynch answer is incorrect")
                    _ => false
                }
            }).ToArray();
        }

        public void OnGameSessionCreated(GameSession session) =>
            _bot.CreateRegistrationMessage(session, _settings);

        public async void OnGamerJoined(GameSession session, GamerAccount account)
        {
            await _bot.LockAndDo(async () =>
            {
                await _bot.SendTextMessageAsync(account.PersonalRoomId, "Ты в игре! :)");
            });
            await _bot.UpdateRegistrationMessage(session, _settings);
        }

        public async void OnGamerLeft(GameSession session)
        {
            await _bot.UpdateRegistrationMessage(session, _settings);
        }

        public void OnGameStarted(GameSession session) =>
            _bot.RemoveRegistrationMessage(session);

        public void OnGameRegistrationStopped(GameSession session) =>
            _bot.RemoveRegistrationMessage(session);

        private Task MarkActionAsOutdated(Message message) =>
            _bot.LockAndDo(() => _bot.EditMessageTextAsync(message.Chat.Id, message.MessageId,
                "Поздно. Ты не успел нажать на кнопочки :("));

        private async Task<ActionDescriptor> AskForAction(string text,
            GameSessionMember actionFromMember,
            IEnumerable<GameSessionMember> availableMembers,
            Func<GameSessionMember, int, (GameActions? actionName, string uiText)[]> buttonsTemplateBuilder,
            int secondsToWaitForAnswer = 40)
        {
            var keyboard = availableMembers
                .SelectMany((member, index) =>
                {
                    var template = buttonsTemplateBuilder(member, index + 1);
                    var result = new List<List<InlineKeyboardButton>>();
                    // detecting header button, if exists
                    var currentIndex = 0;
                    if (template[0] is { } header && header.actionName == null)
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

            Message? message = null;
            await _bot.LockAndDo(async () =>
            {
                message = await _bot.SendTextMessageAsync(actionFromMember.GamerAccount.PersonalRoomId,
                    text,
                    ParseMode.Html, false, false, 0, new InlineKeyboardMarkup(keyboard));
            });

            // calculation how much tries with 5sec step
            var tries = secondsToWaitForAnswer / 5;
            GameSessionMember? target = null;

            GameActions? action = null;
            while (tries > 0)
            {
                // sleep and wait for user answer
                await Task.Delay(5000);
                // check answer
                if (TelegramFrontendExtensions.IsActionProvided(actionFromMember.Id))
                {
                    if (!(TelegramFrontendExtensions.GetAction(actionFromMember.Id) is {} actionInfo))
                        continue;

                    var (actionName, gamerId) = actionInfo;
                    TelegramFrontendExtensions.RemoveAction(actionFromMember.Id);
                    target = availableMembers.FirstOrDefault(g => g.Id == gamerId);
                    action = Enum.Parse<GameActions>(actionName);
                    break;
                }

                // try again, and update counter
                tries--;
            }

            // delete action message, because it's outdated.
            if (target == null && message != null) await MarkActionAsOutdated(message);
            return new ActionDescriptor(action, target, actionFromMember);
        }

        public event Action<(int roomId, int gamerId)> GameJoinRequest;
        public event Action<(int roomId, int gamerId)> GameCreationRequest;
        public event Action<(int roomId, int gamerId)> GameStopRequest;
        public event Action<(int roomId, int gamerId)> GameLeaveRequest;
        public event Action<int> GameStartRequest;


        protected virtual void OnGameJoinRequest(int gameRoomId, int gamerAccountId) =>
            GameJoinRequest?.Invoke((gameRoomId, gamerAccountId));

        protected virtual void OnGameCreationRequest(int gameRoomId, int gamerAccountId) =>
            GameCreationRequest?.Invoke((gameRoomId, gamerAccountId));

        protected virtual void OnGameStopRequest(int gameRoomId, int gamerAccountId) =>
            GameStopRequest?.Invoke((gameRoomId, gamerAccountId));

        protected virtual void OnGameStartRequest(int gameRoomId) =>
            GameStartRequest?.Invoke(gameRoomId);

        protected virtual void OnGameLeaveRequest(int gameRoomId, int gamerAccountId) =>
            GameLeaveRequest?.Invoke((gameRoomId, gamerAccountId));
    }
}