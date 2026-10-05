using BookStore.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookStore.Infrastructure.Persistence.Migrations
{
    /// <summary>
    /// Folds the Seller and Buyer roles into the single Member role. Every account
    /// that held either one becomes a member, and the old roles are removed. Accounts
    /// without a seller profile get one the next time they sign in.
    /// </summary>
    [DbContext(typeof(AppDbContext))]
    [Migration("20261004120000_MergeMarketplaceRoles")]
    public partial class MergeMarketplaceRoles : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Roles] WHERE [NormalizedName] IN (N'SELLER', N'BUYER'))
                   AND NOT EXISTS (SELECT 1 FROM [Roles] WHERE [NormalizedName] = N'MEMBER')
                BEGIN
                    INSERT INTO [Roles] ([Id], [Name], [NormalizedName], [Description], [ConcurrencyStamp])
                    VALUES (NEWID(), N'Member', N'MEMBER', N'Buys and sells books through the platform.', CONVERT(nvarchar(36), NEWID()));
                END;

                INSERT INTO [UserRoles] ([UserId], [RoleId])
                SELECT DISTINCT [ur].[UserId], [member].[Id]
                FROM [UserRoles] AS [ur]
                INNER JOIN [Roles] AS [old] ON [old].[Id] = [ur].[RoleId]
                CROSS JOIN (SELECT [Id] FROM [Roles] WHERE [NormalizedName] = N'MEMBER') AS [member]
                WHERE [old].[NormalizedName] IN (N'SELLER', N'BUYER')
                  AND NOT EXISTS (
                      SELECT 1 FROM [UserRoles] AS [existing]
                      WHERE [existing].[UserId] = [ur].[UserId] AND [existing].[RoleId] = [member].[Id]);

                DELETE FROM [Roles] WHERE [NormalizedName] IN (N'SELLER', N'BUYER');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                IF EXISTS (SELECT 1 FROM [Roles] WHERE [NormalizedName] = N'MEMBER')
                BEGIN
                    INSERT INTO [Roles] ([Id], [Name], [NormalizedName], [Description], [ConcurrencyStamp])
                    SELECT NEWID(), [name], UPPER([name]), [description], CONVERT(nvarchar(36), NEWID())
                    FROM (VALUES
                        (N'Seller', N'Lists books for sale through the platform.'),
                        (N'Buyer', N'Buys books through the platform.')) AS [r]([name], [description])
                    WHERE NOT EXISTS (SELECT 1 FROM [Roles] WHERE [NormalizedName] = UPPER([r].[name]));

                    INSERT INTO [UserRoles] ([UserId], [RoleId])
                    SELECT [ur].[UserId], [old].[Id]
                    FROM [UserRoles] AS [ur]
                    INNER JOIN [Roles] AS [member] ON [member].[Id] = [ur].[RoleId] AND [member].[NormalizedName] = N'MEMBER'
                    CROSS JOIN [Roles] AS [old]
                    WHERE [old].[NormalizedName] IN (N'SELLER', N'BUYER');

                    DELETE FROM [Roles] WHERE [NormalizedName] = N'MEMBER';
                END;
                """);
        }
    }
}
