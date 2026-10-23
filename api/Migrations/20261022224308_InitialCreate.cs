using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtmApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Accounts",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    Name = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Balance = table.Column<long>(type: "INTEGER", nullable: false),
                    LastSequence = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Accounts", x => x.Id);
                    table.CheckConstraint("CK_Accounts_Balance", "\"Balance\" >= 0");
                });

            migrationBuilder.CreateTable(
                name: "Transactions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "TEXT", nullable: false),
                    AccountId = table.Column<Guid>(type: "TEXT", nullable: false),
                    Sequence = table.Column<long>(type: "INTEGER", nullable: false),
                    Amount = table.Column<long>(type: "INTEGER", nullable: false),
                    BalanceAfter = table.Column<long>(type: "INTEGER", nullable: false),
                    Type = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CorrelationId = table.Column<Guid>(type: "TEXT", nullable: true),
                    TimestampUtc = table.Column<long>(type: "INTEGER", nullable: false),
                    Description = table.Column<string>(type: "TEXT", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Transactions", x => x.Id);
                    table.CheckConstraint("CK_Transactions_Amount", "\"Amount\" > 0");
                    table.CheckConstraint("CK_Transactions_BalanceAfter", "\"BalanceAfter\" >= 0");
                    table.ForeignKey(
                        name: "FK_Transactions_Accounts_AccountId",
                        column: x => x.AccountId,
                        principalTable: "Accounts",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_Sequence",
                table: "Transactions",
                columns: new[] { "AccountId", "Sequence" },
                unique: true);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_Transactions_NoUpdate" BEFORE UPDATE ON "Transactions"
                BEGIN
                    SELECT RAISE(ABORT, 'Ledger is append-only: transactions cannot be updated.');
                END;
                """);

            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_Transactions_NoDelete" BEFORE DELETE ON "Transactions"
                BEGIN
                    SELECT RAISE(ABORT, 'Ledger is append-only: transactions cannot be deleted.');
                END;
                """);

            // IS NOT rather than <> so an unknown Type (a NULL delta) also fails the check.
            migrationBuilder.Sql(
                """
                CREATE TRIGGER "TR_Transactions_Chain" BEFORE INSERT ON "Transactions"
                WHEN NEW."Sequence" IS NOT 1 + COALESCE(
                        (SELECT MAX("Sequence") FROM "Transactions" WHERE "AccountId" = NEW."AccountId"), 0)
                    OR NEW."BalanceAfter" IS NOT COALESCE(
                        (SELECT "BalanceAfter" FROM "Transactions"
                         WHERE "AccountId" = NEW."AccountId" AND "Sequence" = NEW."Sequence" - 1), 0)
                        + CASE NEW."Type" WHEN 'Deposit' THEN NEW."Amount" WHEN 'TransferIn' THEN NEW."Amount"
                          WHEN 'Withdrawal' THEN -NEW."Amount" WHEN 'TransferOut' THEN -NEW."Amount" END
                BEGIN
                    SELECT RAISE(ABORT, 'Ledger chain broken: a transaction must take the next sequence and carry the previous balance forward by its amount.');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "Transactions");

            migrationBuilder.DropTable(
                name: "Accounts");
        }
    }
}
