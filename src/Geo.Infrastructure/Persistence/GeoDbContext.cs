using Geo.Domain;
using Geo.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using NetTopologySuite;
using NetTopologySuite.Geometries;

namespace Geo.Infrastructure.Persistence;

public class GeoDbContext(DbContextOptions<GeoDbContext> options) : DbContext(options)
{
    public DbSet<TrackedObject> TrackedObjects => Set<TrackedObject>();
    public DbSet<TrackPoint> TrackPoints => Set<TrackPoint>();
    public DbSet<GeoZone> GeoZones => Set<GeoZone>();
    public DbSet<ZoneEvent> ZoneEvents => Set<ZoneEvent>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.HasPostgresExtension("postgis");

        modelBuilder.Entity<TrackedObject>(e =>
        {
            e.ToTable("tracked_objects");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.ExternalId).HasMaxLength(100).IsRequired();
            e.Property(x => x.Kind).HasMaxLength(50).IsRequired();
            e.HasIndex(x => x.ExternalId).IsUnique();
        });

        modelBuilder.Entity<TrackPoint>(e =>
        {
            e.ToTable("track_points");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            // geography: ST_Distance/ST_DWithin в метрах
            e.Property(x => x.Location).HasColumnType("geography(Point, 4326)").IsRequired();
            e.HasOne(x => x.Object)
                .WithMany(o => o.TrackPoints)
                .HasForeignKey(x => x.ObjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => x.Location).HasMethod("gist");
            e.HasIndex(x => new { x.ObjectId, x.RecordedAt });
        });

        modelBuilder.Entity<GeoZone>(e =>
        {
            e.ToTable("geo_zones");
            e.HasKey(x => x.Id);
            e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            e.Property(x => x.Color).HasMaxLength(20);
            e.Property(x => x.Region).HasColumnType("geometry(Polygon, 4326)").IsRequired();
            e.HasIndex(x => x.Region).HasMethod("gist");
        });

        modelBuilder.Entity<ZoneEvent>(e =>
        {
            e.ToTable("zone_events");
            e.HasKey(x => x.Id);
            e.Property(x => x.Id).UseIdentityAlwaysColumn();
            e.Property(x => x.Type).HasConversion<string>().HasMaxLength(10);
            e.Property(x => x.Location).HasColumnType("geography(Point, 4326)").IsRequired();
            e.HasOne(x => x.Object)
                .WithMany(o => o.ZoneEvents)
                .HasForeignKey(x => x.ObjectId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasOne(x => x.Zone)
                .WithMany(z => z.ZoneEvents)
                .HasForeignKey(x => x.ZoneId)
                .OnDelete(DeleteBehavior.Cascade);
            e.HasIndex(x => new { x.ObjectId, x.OccurredAt });
        });
    }

    /// <summary>Фабрика геометрий SRID 4326 для прикладного кода.</summary>
    public static readonly GeometryFactory GeometryFactory =
        NtsGeometryServices.Instance.CreateGeometryFactory(srid: 4326);

    public static Point CreatePoint(double lon, double lat) =>
        GeometryFactory.CreatePoint(new Coordinate(lon, lat));
}
