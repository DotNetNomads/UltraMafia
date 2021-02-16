using System.Collections.Generic;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using Telegram.Bot.Types;
using UltraMafia.Frontend.ComandHandlers;
using UltraMafia.Frontend.Extensions;

namespace UltraMafia.Frontend.Telegram
{
    public interface ITelegramMessageProcessor
    {
        ValueTask HandleMessageAsync(Message message);
    }

    public class TelegramMessageProcessor : ITelegramMessageProcessor
    {
        private readonly INonCommandMessageHandler _defaultHandler;
        private readonly ILogger<TelegramMessageProcessor> _logger;
        private readonly Dictionary<string, ICommandHandler> _commandHandlers;

        public TelegramMessageProcessor(INonCommandMessageHandler defaultHandler,
            ILogger<TelegramMessageProcessor> logger, Dictionary<string, ICommandHandler> commandHandlers)
        {
            _defaultHandler = defaultHandler;
            _logger = logger;
            _commandHandlers = commandHandlers;
        }

        public ValueTask HandleMessageAsync(Message message)
        {
            if (!message.TryParseCommand(out var commandName, out var arguments))
            {
                _logger.LogDebug("Handling non command message: {Msg} from {UserId}", message.Text,
                    message.From.Username ?? message.From.Id.ToString());
                return _defaultHandler.HandleMessageAsync(message);
            }

            if (_commandHandlers.ContainsKey(commandName))
            {
                _logger.LogDebug("Handling command {CommandName} with args {Arguments} from {UserId}",
                    commandName, arguments, message.From.Username ?? message.From.Id.ToString());
                return _commandHandlers[commandName].HandleCommandAsync(arguments, message.Chat, message.From);
            }

            _logger.LogDebug("Can't found handler for command {CommandName} from {UserId}", commandName,
                message.From.Username ?? message.From.Id.ToString());
            return ValueTask.CompletedTask;
        }
    }
}