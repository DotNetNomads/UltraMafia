using Microsoft.EntityFrameworkCore;
using UltraMafia.DAL.Model;

namespace UltraMafia.DAL
{
    public class GameDbContext : DbContext
    {
        public DbSet<GameRoom> GameRooms { get; set; }
        public DbSet<GameSession> GameSessions { get; set; }
        public DbSet<GameSessionMember> GameSessionMembers { get; set; }
        public DbSet<GamerAccount> GamerAccounts { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);
            modelBuilder
                .Entity<GameSession>(gameSession =>
                {
                    gameSession
                        .HasMany(m => m.GameMembers)
                        .WithOne(m => m.GameSession)
                        .HasForeignKey(m => m.GameSessionId);
                    gameSession
                        .HasOne(m => m.Room)
                        .WithMany()
                        .HasForeignKey(m => m.RoomId);
                    gameSession
                        .HasOne(gs => gs.CreatedByGamerAccount)
                        .WithMany()
                        .HasForeignKey(m => m.CreatedByGamerAccountId);
                });
            modelBuilder
                .Entity<GameSessionMember>(sessionMember =>
                {
                    sessionMember
                        .HasOne(m => m.GamerAccount)
                        .WithMany()
                        .HasForeignKey(m => m.GamerAccountId);
                });
        }

        public GameDbContext(DbContextOptions options) : base(options)
        {
        }
    }
}