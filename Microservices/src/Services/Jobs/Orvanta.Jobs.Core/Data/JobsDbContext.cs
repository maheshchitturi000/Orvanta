using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orvanta.Jobs.Core.Domain;

namespace Orvanta.Jobs.Core.Data;

public sealed class JobsDbContext(DbContextOptions<JobsDbContext> options) : DbContext(options)
{
    public DbSet<CandidateProfile> Profiles => Set<CandidateProfile>();
    public DbSet<JobPreference> Preferences => Set<JobPreference>();
    public DbSet<JobPosting> Postings => Set<JobPosting>();
    public DbSet<JobMatch> Matches => Set<JobMatch>();
    public DbSet<JobApplication> Applications => Set<JobApplication>();
    public DbSet<JobSourceConnection> SourceConnections => Set<JobSourceConnection>();

    /// <summary>All timestamps are stored as UTC; read them back as UTC so APIs serialize them with "Z".</summary>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        configurationBuilder.Properties<DateTime>().HaveConversion<UtcDateTimeConverter>();
        configurationBuilder.Properties<DateTime?>().HaveConversion<NullableUtcDateTimeConverter>();
    }

    private sealed class UtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime, DateTime>(
        v => v.Kind == DateTimeKind.Utc ? v : v.ToUniversalTime(),
        v => DateTime.SpecifyKind(v, DateTimeKind.Utc));

    private sealed class NullableUtcDateTimeConverter() : Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<DateTime?, DateTime?>(
        v => v.HasValue ? (v.Value.Kind == DateTimeKind.Utc ? v : v.Value.ToUniversalTime()) : v,
        v => v.HasValue ? DateTime.SpecifyKind(v.Value, DateTimeKind.Utc) : v);

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<CandidateProfile>(e =>
        {
            e.ToTable("CandidateProfiles");
            e.HasKey(x => x.UserId);
            e.Property(x => x.FullName).HasMaxLength(100);
            e.Property(x => x.Email).HasMaxLength(254);
            e.Property(x => x.Phone).HasMaxLength(30);
            e.Property(x => x.Headline).HasMaxLength(200);
            e.Property(x => x.CurrentTitle).HasMaxLength(150);
            e.Property(x => x.Location).HasMaxLength(150);
            e.Property(x => x.TotalYearsExperience).HasPrecision(4, 1);
            e.Property(x => x.Summary).HasMaxLength(2000);
            e.Property(x => x.LinkedInUrl).HasMaxLength(300);
            e.Property(x => x.PortfolioUrl).HasMaxLength(300);
            e.Property(x => x.CvFileName).HasMaxLength(200);
            e.Property(x => x.CvContentType).HasMaxLength(100);
            JsonList(e.Property(x => x.Skills));
        });

        modelBuilder.Entity<JobSourceConnection>(e =>
        {
            e.ToTable("JobSourceConnections");
            e.HasKey(x => new { x.UserId, x.Source });
            e.Property(x => x.Source).HasMaxLength(30);
            e.Property(x => x.SecretEncrypted).HasMaxLength(1000).IsRequired();
        });

        modelBuilder.Entity<JobPreference>(e =>
        {
            e.ToTable("JobPreferences");
            e.HasKey(x => x.UserId);
            e.Property(x => x.WorkAuthorization).HasMaxLength(200);
            e.Property(x => x.ExpectedSalary).HasMaxLength(100);
            e.Property(x => x.TimeZoneId).HasMaxLength(100);
            e.Property(x => x.LastDiscoverySummary).HasMaxLength(500);
            JsonList(e.Property(x => x.DesiredTitles));
            JsonList(e.Property(x => x.Locations));
            JsonList(e.Property(x => x.CompanyBoards));
            JsonList(e.Property(x => x.ExcludedCompanies));
        });

        modelBuilder.Entity<JobPosting>(e =>
        {
            e.ToTable("JobPostings");
            e.HasKey(x => x.Id);
            e.Property(x => x.Source).HasMaxLength(30).IsRequired();
            e.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();
            e.HasIndex(x => new { x.Source, x.ExternalId }).IsUnique();
            e.Property(x => x.Company).HasMaxLength(150);
            e.Property(x => x.Title).HasMaxLength(300);
            e.Property(x => x.Location).HasMaxLength(300);
            e.Property(x => x.Url).HasMaxLength(500);
            e.Property(x => x.ApplyUrl).HasMaxLength(500);
        });

        modelBuilder.Entity<JobMatch>(e =>
        {
            e.ToTable("JobMatches");
            e.HasKey(x => x.Id);
            e.HasIndex(x => new { x.UserId, x.JobPostingId }).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Status, x.Score });
            e.HasOne(x => x.JobPosting).WithMany().HasForeignKey(x => x.JobPostingId);
            JsonList(e.Property(x => x.MatchedSkills));
            JsonList(e.Property(x => x.MissingSkills));
            JsonList(e.Property(x => x.Reasons));
        });

        modelBuilder.Entity<JobApplication>(e =>
        {
            e.ToTable("JobApplications");
            e.HasKey(x => x.Id);
            e.HasIndex(x => x.JobMatchId).IsUnique();
            e.HasIndex(x => new { x.UserId, x.Status });
            e.HasOne(x => x.JobPosting).WithMany().HasForeignKey(x => x.JobPostingId).OnDelete(DeleteBehavior.NoAction);
            e.Property(x => x.Channel).HasMaxLength(20);
            e.Property(x => x.Error).HasMaxLength(1000);
        });
    }

    /// <summary>Stores a string list as a JSON column with proper change tracking.</summary>
    private static void JsonList(PropertyBuilder<List<string>> property) =>
        property
            .HasConversion(
                v => JsonSerializer.Serialize(v, (JsonSerializerOptions?)null),
                v => JsonSerializer.Deserialize<List<string>>(v, (JsonSerializerOptions?)null) ?? new List<string>())
            .Metadata.SetValueComparer(new ValueComparer<List<string>>(
                (a, b) => a!.SequenceEqual(b!),
                v => v.Aggregate(0, (hash, item) => HashCode.Combine(hash, item.GetHashCode())),
                v => v.ToList()));
}
