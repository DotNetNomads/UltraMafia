using System;
using Microsoft.Extensions.DependencyInjection;

namespace UltraMafia.Helpers
{
    public static class DiExtensions
    {
        public static DbContextAccessor GetDbContext(this IServiceProvider serviceProvider) =>
            new DbContextAccessor(serviceProvider.CreateScope());
    }
}