using Microsoft.EntityFrameworkCore;
using ScamAlert.Data.Entities;

namespace ScamAlert.Data;

public sealed class ScamAlertDbContext(DbContextOptions<ScamAlertDbContext> options) : DbContext(options)
{
    public DbSet<Customer> Customers => Set<Customer>();
    public DbSet<Contact> Contacts => Set<Contact>();
    public DbSet<MonitoredDevice> Devices => Set<MonitoredDevice>();
    public DbSet<Subscription> Subscriptions => Set<Subscription>();
    public DbSet<AlertEvent> AlertEvents => Set<AlertEvent>();
    public DbSet<NotificationAttempt> NotificationAttempts => Set<NotificationAttempt>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Email).HasMaxLength(320);
        });

        modelBuilder.Entity<Contact>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.FullName).HasMaxLength(200);
            entity.Property(x => x.PhoneNumber).HasMaxLength(32);
            entity.HasIndex(x => new { x.CustomerId, x.EscalationOrder }).IsUnique();
        });

        modelBuilder.Entity<MonitoredDevice>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DeviceName).HasMaxLength(200);
            entity.Property(x => x.ExternalDeviceId).HasMaxLength(100);
            entity.HasIndex(x => x.ExternalDeviceId).IsUnique();
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PlanCode).HasMaxLength(100);
        });

        modelBuilder.Entity<AlertEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceIp).HasMaxLength(64);
            entity.Property(x => x.Service).HasMaxLength(32);
            entity.HasIndex(x => new { x.DeviceId, x.CreatedUtc });
        });

        modelBuilder.Entity<NotificationAttempt>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Channel).HasMaxLength(32);
            entity.Property(x => x.ProviderMessageId).HasMaxLength(100);
            entity.Property(x => x.AcknowledgmentToken).HasMaxLength(32);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => x.AcknowledgmentToken).IsUnique();
            entity.HasIndex(x => new { x.AlertEventId, x.AttemptedUtc });
        });
    }
}
