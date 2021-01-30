using System;
using System.Linq;
using System.Text.RegularExpressions;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using UltraMafia.Common.Events;

namespace UltraMafia.Frontend.Extensions
{
    internal static class MessageExtension
    {
        public static void EnsurePublicChat(this Message message)
        {
            if (message.IsPublicChat())
                return;
            throw new InvalidOperationException("Это действие разрешено вызывать только из публичных чатов");
        }

        public static bool IsPublicChat(this Message message) =>
            message.Chat.Type == ChatType.Group || message.Chat.Type == ChatType.Supergroup;

        public static RoomInfo? ResolveRoomInfo(this Message message) => message.IsPublicChat()
            ? null
            : new RoomInfo(message.Chat.Id.ToString(), message.Chat.Title);

        public static GamerInfo ResolveGamerInfo(this Message message)
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
            return new GamerInfo(userId, nickName, userChatId);
        }

        public static bool TryParseCommand(this Message message, out string commandName, out string[] arguments)
        {
            string[] splitMessage;
            var text = message.Text;
            if (text.StartsWith("/") && Regex.IsMatch((splitMessage = text.Split(" "))[0],
                "^[a-zA-Z0-9]*$"))
            {
                commandName = splitMessage[0].Remove(1);
                arguments = splitMessage.Skip(1).ToArray();
                return true;
            }

            commandName = string.Empty;
            arguments = Array.Empty<string>();
            return false;
        }
    }
}