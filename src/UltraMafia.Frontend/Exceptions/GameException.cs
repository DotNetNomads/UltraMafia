using System;

namespace UltraMafia.Frontend.Exceptions
{
    /// <summary>
    /// Internal game exception
    /// </summary>
    public class GameException : Exception
    {
        public GameException(string? message) : base(message)
        {
        }
    }
}