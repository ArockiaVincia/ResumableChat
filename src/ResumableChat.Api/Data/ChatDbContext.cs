using Microsoft.EntityFrameworkCore;
using ResumableChat.Api.Data.Entities;

namespace ResumableChat.Api.Data;

public class ChatDbContext : DbContext
{
    public ChatDbContext(DbContextOptions<ChatDbContext> options) : base(options)
    {
    }

    public DbSet<Conversation> Conversations => Set<Conversation>();
    public DbSet<Run> Runs => Set<Run>();
    public DbSet<ChatEvent> Events => Set<ChatEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Conversation>(entity =>
        {
            entity.HasKey(c => c.Id);
            entity.Property(c => c.CreatedAt).IsRequired();
        });

        modelBuilder.Entity<Run>(entity =>
        {
            entity.HasKey(r => r.Id);
            entity.Property(r => r.ConversationId).IsRequired();
            entity.Property(r => r.UserMessageId).IsRequired();
            entity.Property(r => r.Status).IsRequired();
            entity.Property(r => r.CreatedAt).IsRequired();

            entity.HasOne(r => r.Conversation)
                .WithMany(c => c.Runs)
                .HasForeignKey(r => r.ConversationId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChatEvent>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.RunId).IsRequired();
            entity.Property(e => e.Sequence).IsRequired();
            entity.Property(e => e.Type).IsRequired();
            entity.Property(e => e.Text).IsRequired();
            entity.Property(e => e.CreatedAt).IsRequired();

            entity.HasIndex(e => new { e.RunId, e.Sequence })
                .IsUnique();

            entity.HasOne(e => e.Run)
                .WithMany(r => r.Events)
                .HasForeignKey(e => e.RunId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
