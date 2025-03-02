using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Chat.Infrastructure.ReadModel.Migrations
{
    /// <inheritdoc />
    public partial class AddSyncFeedAndDeliveries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Deliveries",
                schema: "read",
                columns: table => new
                {
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    UserName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    DeviceId = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    LastDeliveredMessageId = table.Column<long>(type: "bigint", nullable: false),
                    DeliveredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Deliveries", x => new { x.RoomId, x.UserName, x.DeviceId });
                });

            migrationBuilder.CreateTable(
                name: "State",
                schema: "read",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false),
                    Epoch = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_State", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "SyncEntries",
                schema: "read",
                columns: table => new
                {
                    Position = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    RoomId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    MessageId = table.Column<long>(type: "bigint", nullable: true),
                    UserName = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: true),
                    OccurredAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SyncEntries", x => x.Position);
                });

            migrationBuilder.InsertData(
                schema: "read",
                table: "State",
                columns: new[] { "Id", "Epoch" },
                values: new object[] { 1, new Guid("5d0f3c1e-7b2a-4e9d-8c61-2f4a9b7e3d10") });

            migrationBuilder.CreateIndex(
                name: "IX_SyncEntries_RoomId_Position",
                schema: "read",
                table: "SyncEntries",
                columns: new[] { "RoomId", "Position" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Deliveries",
                schema: "read");

            migrationBuilder.DropTable(
                name: "State",
                schema: "read");

            migrationBuilder.DropTable(
                name: "SyncEntries",
                schema: "read");
        }
    }
}
