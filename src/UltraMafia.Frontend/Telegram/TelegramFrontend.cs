using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using JKang.EventBus;
using Serilog;
using Telegram.Bot;
using Telegram.Bot.Args;
using Telegram.Bot.Exceptions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using Telegram.Bot.Types.ReplyMarkups;
using UltraMafia.Common.Config;
using UltraMafia.Common.Events;
using UltraMafia.Common.GameModel;
using UltraMafia.DAL.Enums;
using UltraMafia.DAL.Model;
using UltraMafia.Frontend.Events;
using UltraMafia.Frontend.Extensions;
using UltraMafia.Frontend.Telegram.Config;
using static UltraMafia.DAL.Enums.GameActions;

namespace UltraMafia.Frontend.Telegram
{
    internal sealed class TelegramMessageProcessor
    {
        private record CommandHandler(string Command, bool PublicChat, Func<Message, Task> Handler);

        private readonly List<CommandHandler> _commandHandlers = new();
        private Func<Message, Task> _defaultHandler;

        public TelegramMessageProcessor(Func<Message, Task> defaultHandler)
        {
            _defaultHandler = defaultHandler;
        }

        public TelegramMessageProcessor Command(string command, bool publicChat, Func<Message, Task> handler)
        {
            _commandHandlers.Add(new CommandHandler(command, publicChat, handler));
            return this;
        }

        public Task HandleMessage(Message message)
        {
            
        }
    }

    public class TelegramFrontend
    {
        private DateTime _startTime;
        private readonly TelegramFrontendSettings _settings;
        private readonly TelegramBotClient _bot;
        private readonly IEventPublisher _eventPublisher;
        private readonly TelegramMessageProcessor _messageProcessor = new TelegramMessageProcessor();


        public TelegramFrontend(TelegramFrontendSettings settings, IServiceProvider serviceProvider,
            GameSettings gameSettings, IEventPublisher eventPublisher)
        {
            _settings = settings;
            _eventPublisher = eventPublisher;
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
                await HandleVoteAnswer(callBackQuery, requestArgs);
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

        private async Task HandleVoteAnswer(CallbackQuery callBackQuery, string[] requestArgs)
        {
            if (requestArgs.Length < 2)
                return;
            callBackQuery.Message.EnsurePublicChat();
            var roomId = callBackQuery.Message.Chat.Id.ToString();
            var userId = callBackQuery.From.Id.ToString();
            var voice = requestArgs[1];
            await _eventPublisher.PublishEventAsync(new VoteAnswerReceivedEvent(roomId, userId, voice,
                callBackQuery.Id));
            return;
        }

        private Task BotOnMessageReceived(Message message)
        {
            // skip old messages
            if (message.Date < _startTime || message.Type != MessageType.Text)
                return Task.CompletedTask;

            // trying to parse bot commands
            var text = message.Text;

            bool CommandMatch(string name) => text == $"/{name}" || text == $"/{name}@{_settings.BotUserName}";
            if (text.StartsWith("/start"))
                return ProcessJoinCommand(message);

            if (CommandMatch("go"))
                return ProcessGoCommand(message);

            if (CommandMatch("game"))
                return ProcessGameCommand(message);

            if (CommandMatch("stop"))
                return _eventPublisher.PublishEventAsync(new GameStopRequestEvent(message.ResolveGamerInfo(),
                    message.ResolveRoomInfo()));

            if (CommandMatch("leave"))
                return _eventPublisher.PublishEventAsync(new GamerLeaveRequestEvent(
                    message.ResolveRoomInfo(),
                    message.ResolveGamerInfo()));

            return ProcessMessageDefault(message);
        }

        #endregion

        #region Actions

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


            OnGameJoinRequest(roomId, gamerAccountId);
        }

        private Task ProcessMessageDefault(Message message)
        {
            var chatId = message.Chat.Id.ToString();
            var isPersonal = message.Chat.Type == ChatType.Private;
            // mb last words?
            if (isPersonal && TelegramFrontendExtensions.IsLastWordsActual(chatId))
                TelegramFrontendExtensions.SaveLastWords(chatId, message.Text);
        }

        #endregion

        // public void ActivateFrontend()
        // {
        //     _startTime = DateTime.UtcNow;
        //     _bot.StartReceiving(new[]
        //     {
        //         UpdateType.CallbackQuery, UpdateType.Message
        //     });
        //
        //     _bot.OnUpdate += BotOnOnUpdate;
        // }

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
                    if (!(TelegramFrontendExtensions.GetAction(actionFromMember.Id) is { } actionInfo))
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
    }
}