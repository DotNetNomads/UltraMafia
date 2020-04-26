using System;
using System.Collections.Generic;
using System.Linq;

namespace UltraMafia
{
    public static class EnumerableExtensions
    {
        public static T Random<T>(this IEnumerable<T> enumerable)
        {
            var r = new Random();
            var list = enumerable.ToList();
            return list.ElementAt(r.Next(0, list.Count));
        }
    }
}