using System;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using UltraMafia.DAL;

namespace UltraMafia.Helpers
{
    public class DbContextAccessor : IDisposable
    {
        private IServiceScope _scope;
        public GameDbContext DbContext { get; }

        public DbContextAccessor(IServiceScope scope)
        {
            _scope = scope;
            DbContext = scope.ServiceProvider.GetService<GameDbContext>();
        }

        public void Dispose()
        {
            _scope.Dispose();
        }
    }
}