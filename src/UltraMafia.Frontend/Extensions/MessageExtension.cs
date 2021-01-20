using System;
using Telegram.Bot.Types;
using Telegram.Bot.Types.Enums;
using UltraMafia.Common.Events;

namespace UltraMafia.Frontend.Extensions
{
    internal static class MessageExtension
    {
        public static void EnsurePublicChat(this Message message)
        {
            if (message.Chat.Type != ChatType.Private && message.Chat.Type != ChatType.Channel)
                return;
            throw new InvalidOperationException("Это действие разрешено вызывать только из публичных чатов");
        }

        public static RoomInfo ResolveRoomInfo(this Message message)
        {
            message.EnsurePublicChat();
            return new RoomInfo(message.Chat.Id.ToString(), message.Chat.Title);
        }

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
    }
}