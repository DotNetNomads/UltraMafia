using System;
using System.Threading;
using System.Threading.Tasks;
using Serilog;
using Telegram.Bot;

namespace UltraMafia.Frontend.Extensions
{
    public static class BotExtensions
    {
        private static readonly SemaphoreSlim BotLock = new SemaphoreSlim(1);

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
    }
}