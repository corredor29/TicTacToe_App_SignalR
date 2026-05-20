using Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Infrastructure.Persistence;

public class TicTacToeDbContext : DbContext
{
    public TicTacToeDbContext(DbContextOptions<TicTacToeDbContext> options) : base(options) { }

    public DbSet<User> Users { get; set; }
    public DbSet<GameSessionRecord> GameSessions { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>().ToTable("users");
        modelBuilder.Entity<GameSessionRecord>(entity =>
        {
            entity.ToTable("game_sessions");
            entity.HasIndex(x => x.RoomName);
            entity.HasIndex(x => new { x.RoomName, x.IsActive });
        });
    }
}
