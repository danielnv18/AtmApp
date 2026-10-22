using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtmApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AppendOnlyLedger : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<long>(
                name: "Sequence",
                table: "Transactions",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            migrationBuilder.AddColumn<long>(
                name: "LastSequence",
                table: "Accounts",
                type: "INTEGER",
                nullable: false,
                defaultValue: 0L);

            // Number existing rows in the order history used to show them (timestamp, then
            // insertion order), so each account's chain starts at 1 with no gaps.
            migrationBuilder.Sql(
                """
                UPDATE "Transactions" SET "Sequence" = (
                    SELECT COUNT(*) FROM "Transactions" p
                    WHERE p."AccountId" = "Transactions"."AccountId"
                      AND (p."TimestampUtc" < "Transactions"."TimestampUtc"
                        OR (p."TimestampUtc" = "Transactions"."TimestampUtc" AND p.rowid <= "Transactions".rowid)));
                """);

            migrationBuilder.Sql(
                """
                UPDATE "Accounts" SET "LastSequence" = (
                    SELECT COUNT(*) FROM "Transactions" t WHERE t."AccountId" = "Accounts"."Id");
                """);

            migrationBuilder.DropColumn(
                name: "ConcurrencyVersion",
                table: "Accounts");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_AccountId_TimestampUtc",
                table: "Transactions");

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_Sequence",
                table: "Transactions",
                columns: new[] { "AccountId", "Sequence" },
                unique: true);

            // Created last: a SQLite table rebuild earlier in this migration would drop them.
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
                        + CASE NEW."Type" WHEN 'Deposit' THEN NEW."Amount" WHEN 'Withdrawal' THEN -NEW."Amount" END
                BEGIN
                    SELECT RAISE(ABORT, 'Ledger chain broken: a transaction must take the next sequence and carry the previous balance forward by its amount.');
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER \"TR_Transactions_Chain\";");
            migrationBuilder.Sql("DROP TRIGGER \"TR_Transactions_NoDelete\";");
            migrationBuilder.Sql("DROP TRIGGER \"TR_Transactions_NoUpdate\";");

            migrationBuilder.DropIndex(
                name: "IX_Transactions_AccountId_Sequence",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "Sequence",
                table: "Transactions");

            migrationBuilder.DropColumn(
                name: "LastSequence",
                table: "Accounts");

            migrationBuilder.AddColumn<Guid>(
                name: "ConcurrencyVersion",
                table: "Accounts",
                type: "TEXT",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.CreateIndex(
                name: "IX_Transactions_AccountId_TimestampUtc",
                table: "Transactions",
                columns: new[] { "AccountId", "TimestampUtc" },
                descending: new[] { false, true });
        }
    }
}
