using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Telegram.Bot.Types;
using UltraMafia.Common.Events;
using UltraMafia.Frontend.Extensions;
using UltraMafia.Frontend.Telegram.Config;

namespace UltraMafia.Frontend.Telegram
{
    public interface ITelegramMessageProcessor
    {
        Func<Message, ValueTask>? DefaultHandler { set; }

        TelegramMessageProcessor Command(string command, bool publicChat,
            Func<Message, string[]?, ValueTask> handler);

        ValueTask HandleMessageAsync(Message message);
    }

    public class TelegramMessageProcessor : ITelegramMessageProcessor
    {
        private record CommandHandler(string Command, bool PublicChat,
            Func<Message, string[]?, ValueTask> Handler);

        private readonly List<CommandHandler> _commandHandlers = new();
        public Func<Message, ValueTask>? DefaultHandler { private get; set; }
        private readonly TelegramFrontendSettings _settings;

        public TelegramMessageProcessor(TelegramFrontendSettings settings) => _settings = settings;

        public TelegramMessageProcessor Command(string command, bool publicChat,
            Func<Message, string[]?, ValueTask> handler)
        {
            _commandHandlers.Add(new CommandHandler(command, publicChat, handler));
            return this;
        }

        private bool CommandMatch(string name, string text) =>
            text == $"/{name}" || text == $"/{name}@{_settings.BotUserName}";

        public ValueTask HandleMessageAsync(Message message)
        {
            var publicChat = message.IsPublicChat();
            var handler = _commandHandlers.FirstOrDefault(ch =>
                ch.PublicChat == publicChat && CommandMatch(ch.Command, message.Text));

            if (handler != null)
                return handler.Handler.Invoke(message,
                    null);

            if (DefaultHandler == null)
                throw new InvalidOperationException("Please provide default handler for messages");

            return DefaultHandler.Invoke(message);
        }
    }
}