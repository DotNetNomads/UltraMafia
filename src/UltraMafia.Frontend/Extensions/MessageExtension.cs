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
            return new GamerInfo();
        }
    }
}