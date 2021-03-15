using System;
using Microsoft.Extensions.DependencyInjection;

namespace UltraMafia.DAL.Extensions
{
    public static class DiExtensions
    {
        public static DbContextAccessor GetDbContext(this IServiceProvider serviceProvider) =>
            new DbContextAccessor(serviceProvider.CreateScope());
    }
}