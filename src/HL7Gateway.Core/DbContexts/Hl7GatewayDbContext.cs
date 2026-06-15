using HL7Gateway.Core.Entities;
using Microsoft.EntityFrameworkCore;

namespace HL7Gateway.Core.DbContexts;

public class Hl7GatewayDbContext : DbContext
{
    public Hl7GatewayDbContext(DbContextOptions<Hl7GatewayDbContext> options) : base(options) { }

    public DbSet<Patient> Patients => Set<Patient>();
    public DbSet<Visit> Visits => Set<Visit>();
    public DbSet<Hl7Message> Hl7Messages => Set<Hl7Message>();
    public DbSet<ParsedSegment> ParsedSegments => Set<ParsedSegment>();
    public DbSet<Observation> Observations => Set<Observation>();
    public DbSet<VitalSign> VitalSigns => Set<VitalSign>();
    public DbSet<IdentifierMapping> IdentifierMappings => Set<IdentifierMapping>();
    public DbSet<AdtQueueItem> AdtQueue => Set<AdtQueueItem>();
    public DbSet<AdtLogEntry> AdtLogs => Set<AdtLogEntry>();
    public DbSet<DeviceConnection> DeviceConnections => Set<DeviceConnection>();
    public DbSet<SystemLogEntry> SystemLogs => Set<SystemLogEntry>();
    public DbSet<WsiSubscription> WsiSubscriptions => Set<WsiSubscription>();
    public DbSet<User> Users => Set<User>();
    public DbSet<AutoAdtBed> AutoAdtBeds => Set<AutoAdtBed>();
    public DbSet<AutoAdtBinding> AutoAdtBindings => Set<AutoAdtBinding>();
    public DbSet<AutoAdtEvent> AutoAdtEvents => Set<AutoAdtEvent>();
    public DbSet<AutoAdtMessage> AutoAdtMessages => Set<AutoAdtMessage>();
    public DbSet<AutoAdtScanRule> AutoAdtScanRules => Set<AutoAdtScanRule>();
    public DbSet<AutoAdtSetting> AutoAdtSettings => Set<AutoAdtSetting>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Hl7Message>(e =>
        {
            e.HasIndex(x => x.MessageControlId).IsUnique();
            e.HasIndex(x => x.MessageType);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.ReceivedAt);
            e.HasIndex(x => x.SourceIp);
        });

        modelBuilder.Entity<Observation>(e =>
        {
            e.HasIndex(x => x.IdentifierCode);
            e.HasIndex(x => x.IdentifierText);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.ObservationDateTime);
        });

        modelBuilder.Entity<VitalSign>(e =>
        {
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.VitalSignType);
            e.HasIndex(x => x.ObservationDateTime);
            e.HasIndex(x => new { x.PatientId, x.VitalSignType, x.ObservationDateTime });
        });

        modelBuilder.Entity<IdentifierMapping>(e =>
        {
            e.HasIndex(x => new { x.SourceSystem, x.SourceCode }).IsUnique();
            e.HasIndex(x => x.VitalSignType);
        });

        modelBuilder.Entity<AdtQueueItem>(e =>
        {
            e.HasIndex(x => x.Status);
            e.HasIndex(x => x.NextRetryAt);
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<AutoAdtBed>(e =>
        {
            e.HasIndex(x => x.DeviceCode);
            e.HasIndex(x => x.DeviceBarcode);
            e.HasIndex(x => x.BedBarcode);
            e.HasIndex(x => x.PhilipsLocationValue);
            e.HasIndex(x => x.IsEnabled);
        });

        modelBuilder.Entity<AutoAdtBinding>(e =>
        {
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.VisitId);
            e.HasIndex(x => x.BedId);
            e.HasIndex(x => x.BindingStatus);
            e.HasIndex(x => new { x.BedId, x.BindingStatus });
            e.HasIndex(x => new { x.VisitId, x.BindingStatus });
        });

        modelBuilder.Entity<AutoAdtEvent>(e =>
        {
            e.HasIndex(x => x.EventType);
            e.HasIndex(x => x.PatientId);
            e.HasIndex(x => x.VisitId);
            e.HasIndex(x => x.MessageControlId);
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<AutoAdtMessage>(e =>
        {
            e.HasIndex(x => x.EventId);
            e.HasIndex(x => x.AdtQueueId);
            e.HasIndex(x => x.SendStatus);
            e.HasIndex(x => x.MessageControlId);
            e.HasIndex(x => x.CreatedAt);
        });

        modelBuilder.Entity<AutoAdtScanRule>(e =>
        {
            e.HasIndex(x => x.RuleType);
            e.HasIndex(x => new { x.RuleType, x.IsEnabled, x.Priority });
        });
    }
}
