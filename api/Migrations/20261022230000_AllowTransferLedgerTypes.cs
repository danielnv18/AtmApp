using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AtmApp.Api.Migrations
{
    /// <inheritdoc />
    public partial class AllowTransferLedgerTypes : Migration
    {
        private const string ChainTrigger = """
            CREATE TRIGGER "TR_Transactions_Chain" BEFORE INSERT ON "Transactions"
            WHEN NEW."Sequence" IS NOT 1 + COALESCE(
                    (SELECT MAX("Sequence") FROM "Transactions" WHERE "AccountId" = NEW."AccountId"), 0)
                OR NEW."BalanceAfter" IS NOT COALESCE(
                    (SELECT "BalanceAfter" FROM "Transactions"
                     WHERE "AccountId" = NEW."AccountId" AND "Sequence" = NEW."Sequence" - 1), 0)
                    + CASE NEW."Type" {0} END
            BEGIN
                SELECT RAISE(ABORT, 'Ledger chain broken: a transaction must take the next sequence and carry the previous balance forward by its amount.');
            END;
            """;

        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER \"TR_Transactions_Chain\";");
            migrationBuilder.Sql(string.Format(ChainTrigger,
                """
                WHEN 'Deposit' THEN NEW."Amount" WHEN 'TransferIn' THEN NEW."Amount"
                              WHEN 'Withdrawal' THEN -NEW."Amount" WHEN 'TransferOut' THEN -NEW."Amount"
                """));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TRIGGER \"TR_Transactions_Chain\";");
            migrationBuilder.Sql(string.Format(ChainTrigger,
                """WHEN 'Deposit' THEN NEW."Amount" WHEN 'Withdrawal' THEN -NEW."Amount" """));
        }
    }
}
