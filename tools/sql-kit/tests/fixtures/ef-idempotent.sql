IF OBJECT_ID(N'[demo].[__EFMigrationsHistory]') IS NULL
BEGIN
    IF SCHEMA_ID(N'demo') IS NULL EXEC(N'CREATE SCHEMA [demo];');
    CREATE TABLE [demo].[__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [demo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260101000000_InitialCreate'
)
BEGIN
    CREATE TABLE [demo].[Items] (
        [Id] int NOT NULL,
        [Name] nvarchar(200) NOT NULL,
        CONSTRAINT [PK_Items] PRIMARY KEY ([Id])
    );
END;

IF NOT EXISTS (
    SELECT * FROM [demo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260101000000_InitialCreate'
)
BEGIN
    EXEC(N'INSERT INTO [demo].[Items] ([Id], [Name])
    VALUES (1, N''بند تجريبي''),
    (2, N''It''''s a test'')');
END;

IF NOT EXISTS (
    SELECT * FROM [demo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260101000000_InitialCreate'
)
BEGIN
    INSERT INTO [demo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260101000000_InitialCreate', N'10.0.14');
END;

COMMIT;
GO

BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [demo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260102000000_RenameColumn'
)
BEGIN
    DECLARE @var nvarchar(max);
    SELECT @var = QUOTENAME([d].[name])
    FROM [sys].[default_constraints] [d]
    INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
    WHERE ([d].[parent_object_id] = OBJECT_ID(N'[demo].[Items]') AND [c].[name] = N'Name');
    IF @var IS NOT NULL EXEC(N'ALTER TABLE [demo].[Items] DROP CONSTRAINT ' + @var + ';');
    ALTER TABLE [demo].[Items] ALTER COLUMN [Name] nvarchar(300) NOT NULL;
END;

IF NOT EXISTS (
    SELECT * FROM [demo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260102000000_RenameColumn'
)
BEGIN
    EXEC(N'CREATE OR ALTER VIEW [demo].[ItemNames] AS
    SELECT [Name] FROM [demo].[Items];
    ')
END;

IF NOT EXISTS (
    SELECT * FROM [demo].[__EFMigrationsHistory]
    WHERE [MigrationId] = N'20260102000000_RenameColumn'
)
BEGIN
    INSERT INTO [demo].[__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20260102000000_RenameColumn', N'10.0.14');
END;

COMMIT;
GO

