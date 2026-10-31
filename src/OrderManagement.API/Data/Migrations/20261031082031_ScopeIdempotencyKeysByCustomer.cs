using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace OrderManagement.API.Data.Migrations
{
    /// <inheritdoc />
    public partial class ScopeIdempotencyKeysByCustomer : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_IdempotencyRecords",
                table: "IdempotencyRecords");

            migrationBuilder.AddColumn<Guid>(
                name: "CustomerId",
                table: "IdempotencyRecords",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            // Pending rows can only be leftovers from a crash: new ones commit with their order.
            migrationBuilder.Sql("""DELETE FROM "IdempotencyRecords" WHERE "ResponseStatus" = 0;""");

            // Completed rows must keep replaying for their customer, or a retry would create a second order.
            migrationBuilder.Sql(
                """
                UPDATE "IdempotencyRecords"
                SET "CustomerId" = ("ResponseBody"::jsonb ->> 'customerId')::uuid;
                """);

            migrationBuilder.AddPrimaryKey(
                name: "PK_IdempotencyRecords",
                table: "IdempotencyRecords",
                columns: new[] { "CustomerId", "Key" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_IdempotencyRecords",
                table: "IdempotencyRecords");

            // A key may now be shared by customers; the old primary key allows only the oldest to stay.
            migrationBuilder.Sql(
                """
                DELETE FROM "IdempotencyRecords" newer
                USING "IdempotencyRecords" older
                WHERE newer."Key" = older."Key"
                  AND (newer."CreatedAt", newer."CustomerId") > (older."CreatedAt", older."CustomerId");
                """);

            migrationBuilder.DropColumn(
                name: "CustomerId",
                table: "IdempotencyRecords");

            migrationBuilder.AddPrimaryKey(
                name: "PK_IdempotencyRecords",
                table: "IdempotencyRecords",
                column: "Key");
        }
    }
}
