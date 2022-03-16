using System;
using Microsoft.Extensions.DependencyInjection;

namespace UltraMafia.DAL.Extensions
{
    public class DbContextAccessor : IDisposable
    {
        private IServiceScope _scope;
        public GameDbContext DbContext { get; }

        public DbContextAccessor(IServiceScope scope)
        {
            _scope = scope;
            DbContext = scope.ServiceProvider.GetService<GameDbContext>() ?? throw new InvalidOperationException();
        }

        public void Dispose()
        {
            _scope.Dispose();
        }
    }
}