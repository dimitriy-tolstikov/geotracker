using System;
using Microsoft.EntityFrameworkCore.Migrations;
using NetTopologySuite.Geometries;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Geo.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:postgis", ",,");

            migrationBuilder.CreateTable(
                name: "geo_zones",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    region = table.Column<Polygon>(type: "geometry(Polygon, 4326)", nullable: false),
                    color = table.Column<string>(type: "character varying(20)", maxLength: 20, nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_geo_zones", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "tracked_objects",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    external_id = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    kind = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_tracked_objects", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "track_points",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    location = table.Column<Point>(type: "geography(Point, 4326)", nullable: false),
                    speed = table.Column<double>(type: "double precision", nullable: true),
                    heading = table.Column<double>(type: "double precision", nullable: true),
                    recorded_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    ingested_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_track_points", x => x.id);
                    table.ForeignKey(
                        name: "fk_track_points_tracked_objects_object_id",
                        column: x => x.object_id,
                        principalTable: "tracked_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "zone_events",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityAlwaysColumn),
                    object_id = table.Column<Guid>(type: "uuid", nullable: false),
                    zone_id = table.Column<Guid>(type: "uuid", nullable: false),
                    type = table.Column<string>(type: "character varying(10)", maxLength: 10, nullable: false),
                    occurred_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    location = table.Column<Point>(type: "geography(Point, 4326)", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_zone_events", x => x.id);
                    table.ForeignKey(
                        name: "fk_zone_events_geo_zones_zone_id",
                        column: x => x.zone_id,
                        principalTable: "geo_zones",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "fk_zone_events_tracked_objects_object_id",
                        column: x => x.object_id,
                        principalTable: "tracked_objects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_geo_zones_region",
                table: "geo_zones",
                column: "region")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_track_points_location",
                table: "track_points",
                column: "location")
                .Annotation("Npgsql:IndexMethod", "gist");

            migrationBuilder.CreateIndex(
                name: "ix_track_points_object_id_recorded_at",
                table: "track_points",
                columns: new[] { "object_id", "recorded_at" });

            migrationBuilder.CreateIndex(
                name: "ix_tracked_objects_external_id",
                table: "tracked_objects",
                column: "external_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_zone_events_object_id_occurred_at",
                table: "zone_events",
                columns: new[] { "object_id", "occurred_at" });

            migrationBuilder.CreateIndex(
                name: "ix_zone_events_zone_id",
                table: "zone_events",
                column: "zone_id");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "track_points");

            migrationBuilder.DropTable(
                name: "zone_events");

            migrationBuilder.DropTable(
                name: "geo_zones");

            migrationBuilder.DropTable(
                name: "tracked_objects");
        }
    }
}
