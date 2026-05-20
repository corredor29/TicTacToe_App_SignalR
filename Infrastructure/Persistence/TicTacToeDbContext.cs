using Domain.Entities;
using Domain.Enums;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class TicTacToeDbContext : DbContext
{
    public TicTacToeDbContext(DbContextOptions<TicTacToeDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<GameSessionRecord> GameSessions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");
            entity.Property(x => x.StatusId).HasDefaultValue((int)UserAvailabilityStatus.Available);
        });

        modelBuilder.Entity<GameSessionRecord>(entity =>
        {
            entity.ToTable("game_sessions");
            entity.HasIndex(x => x.RoomName);
            entity.HasIndex(x => new { x.RoomName, x.IsActive });
        });
    }
}
