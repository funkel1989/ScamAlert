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
    public DbSet<AuthUserCredential> AuthUserCredentials => Set<AuthUserCredential>();
    public DbSet<PasswordResetToken> PasswordResetTokens => Set<PasswordResetToken>();
    public DbSet<EmailVerificationToken> EmailVerificationTokens => Set<EmailVerificationToken>();
    public DbSet<DevicePairingCode> DevicePairingCodes => Set<DevicePairingCode>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Customer>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(200);
            entity.Property(x => x.Email).HasMaxLength(320);
            entity.Property(x => x.StripeCustomerId).HasMaxLength(255);
            entity.Property(x => x.BillingLine1).HasMaxLength(200);
            entity.Property(x => x.BillingLine2).HasMaxLength(200);
            entity.Property(x => x.BillingCity).HasMaxLength(100);
            entity.Property(x => x.BillingState).HasMaxLength(50);
            entity.Property(x => x.BillingPostalCode).HasMaxLength(20);
            entity.Property(x => x.BillingCountry).HasMaxLength(2);
            entity.Property(x => x.SignupConsentIpAddress).HasMaxLength(64);
            entity.Property(x => x.SignupLegalDocumentVersion).HasMaxLength(20);
            entity.HasIndex(x => x.StripeCustomerId).IsUnique().HasFilter("[StripeCustomerId] IS NOT NULL");
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
            entity.Property(x => x.IngestApiKeyHash).HasMaxLength(500);
            entity.HasIndex(x => x.ExternalDeviceId).IsUnique();
        });

        modelBuilder.Entity<Subscription>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PlanCode).HasMaxLength(100);
            entity.Property(x => x.StripeSubscriptionId).HasMaxLength(255);
            entity.Property(x => x.StripePriceId).HasMaxLength(255);
            entity.HasIndex(x => x.StripeSubscriptionId).IsUnique().HasFilter("[StripeSubscriptionId] IS NOT NULL");
        });

        modelBuilder.Entity<AlertEvent>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.SourceIp).HasMaxLength(64);
            entity.Property(x => x.Service).HasMaxLength(32);
            entity.Property(x => x.DestinationIp).HasMaxLength(64);
            entity.Property(x => x.Transport).HasMaxLength(16);
            entity.Property(x => x.Direction).HasMaxLength(16);
            entity.Property(x => x.ObservedBy).HasMaxLength(64);
            entity.Property(x => x.RuleApplied).HasMaxLength(128);
            entity.Property(x => x.DecisionReason).HasMaxLength(128);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => new { x.DeviceId, x.CreatedUtc });
            entity.HasIndex(x => new { x.DeviceId, x.ClientEventId })
                .IsUnique()
                .HasFilter("[ClientEventId] IS NOT NULL");

            entity.HasOne(x => x.Customer)
                .WithMany(x => x.AlertEvents)
                .HasForeignKey(x => x.CustomerId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Device)
                .WithMany(x => x.AlertEvents)
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<NotificationAttempt>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Channel).HasMaxLength(32);
            entity.Property(x => x.ProviderMessageId).HasMaxLength(100);
            entity.Property(x => x.AcknowledgmentToken).HasMaxLength(32);
            entity.Property(x => x.Notes).HasMaxLength(500);
            entity.HasIndex(x => x.AcknowledgmentToken)
                .IsUnique()
                .HasFilter("[AcknowledgmentToken] IS NOT NULL");
            entity.HasIndex(x => new { x.AlertEventId, x.AttemptedUtc });

            entity.HasOne(x => x.AlertEvent)
                .WithMany(x => x.NotificationAttempts)
                .HasForeignKey(x => x.AlertEventId)
                .OnDelete(DeleteBehavior.Cascade);

            entity.HasOne(x => x.Contact)
                .WithMany(x => x.NotificationAttempts)
                .HasForeignKey(x => x.ContactId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<AuthUserCredential>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(200);
            entity.Property(x => x.PasswordHash).HasMaxLength(500);
            entity.Property(x => x.RolesCsv).HasMaxLength(500);
            entity.Property(x => x.CustomerScopeCsv).HasMaxLength(2000);
            entity.HasIndex(x => x.Username).IsUnique();
        });

        modelBuilder.Entity<PasswordResetToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(320);
            entity.Property(x => x.TokenHash).HasMaxLength(128);
            entity.HasIndex(x => x.TokenHash);
            entity.HasIndex(x => new { x.Username, x.IsUsed, x.ExpiresUtc });
        });

        modelBuilder.Entity<EmailVerificationToken>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Username).HasMaxLength(320);
            entity.Property(x => x.TokenHash).HasMaxLength(128);
            entity.HasIndex(x => x.TokenHash);
            entity.HasIndex(x => new { x.Username, x.IsUsed, x.ExpiresUtc });
        });

        modelBuilder.Entity<DevicePairingCode>(entity =>
        {
            entity.HasKey(x => x.Id);
            entity.Property(x => x.CodeHash).HasMaxLength(500);
            entity.HasIndex(x => new { x.DeviceId, x.RedeemedUtc, x.ExpiresUtc });

            entity.HasOne(x => x.Device)
                .WithMany()
                .HasForeignKey(x => x.DeviceId)
                .OnDelete(DeleteBehavior.Cascade);
        });
    }
}
