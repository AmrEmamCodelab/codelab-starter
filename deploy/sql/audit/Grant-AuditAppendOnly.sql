/*
  Grant-AuditAppendOnly.sql  (DBA script, idempotent; run after the audit migration and after every release that adds
  a record history table)

  Makes the audit trail and every record history table append-only for the application's database user:
    - GRANT SELECT, INSERT                  on audit.AuditEntries and on every record history table
    - DENY  UPDATE, DELETE                  on the same tables
    - DENY  UPDATE, DELETE ON SCHEMA::audit (covers any later table in the audit schema)

  Record history tables are found by name: every user table whose name ends in "History" (the RecordHistoryEntry
  convention, e.g. smoke.ThingHistory, requests.ServiceRequestHistory), except the EF migrations history tables. Add a table
  that does not follow the convention to the explicit list in batch 2.

  A DENY does not bind db_owner members or sysadmin: the application login must hold neither.

  Set the application's database user in batch 1 (the one line marked SET ME), then run the whole file (SSMS or
  sqlcmd). Every batch is separated by GO and every statement on a table runs through sp_executesql, so the file can
  be re-run safely. The last batch lists the resulting permissions.
*/
SET NOCOUNT ON;
GO

/* Batch 1: the application's database user (SET ME). */
IF OBJECT_ID(N'tempdb..#AuditAppUser') IS NOT NULL DROP TABLE #AuditAppUser;
CREATE TABLE #AuditAppUser (UserName sysname NOT NULL);
INSERT INTO #AuditAppUser (UserName) VALUES (N'starter_app');   -- SET ME: the database user the API connects as
GO

/* Batch 2: the append-only tables. */
IF OBJECT_ID(N'tempdb..#AuditTables') IS NOT NULL DROP TABLE #AuditTables;
CREATE TABLE #AuditTables (SchemaName sysname NOT NULL, TableName sysname NOT NULL);

IF OBJECT_ID(N'[audit].[AuditEntries]', N'U') IS NOT NULL
    INSERT INTO #AuditTables (SchemaName, TableName) VALUES (N'audit', N'AuditEntries');

INSERT INTO #AuditTables (SchemaName, TableName)
SELECT s.name, t.name
FROM sys.tables AS t
JOIN sys.schemas AS s ON s.schema_id = t.schema_id
WHERE t.is_ms_shipped = 0
  AND t.name LIKE N'%History'
  AND t.name <> N'__EFMigrationsHistory';

-- Explicit additions (a history table that does not end in "History"):
-- INSERT INTO #AuditTables (SchemaName, TableName) VALUES (N'<schema>', N'<table>');
GO

/* Batch 3: refuse to run against a missing user or a missing trail. */
DECLARE @user sysname = (SELECT TOP (1) UserName FROM #AuditAppUser);
IF DATABASE_PRINCIPAL_ID(@user) IS NULL
    THROW 50001, N'The application database user named in batch 1 does not exist in this database.', 1;
IF NOT EXISTS (SELECT 1 FROM #AuditTables WHERE SchemaName = N'audit' AND TableName = N'AuditEntries')
    THROW 50002, N'audit.AuditEntries does not exist: apply the audit migration first.', 1;
GO

/* Batch 4: grants and denies (re-running them is harmless). */
DECLARE @user sysname = (SELECT TOP (1) UserName FROM #AuditAppUser);
DECLARE @sql nvarchar(max) = N'';

SELECT @sql = @sql
    + N'GRANT SELECT, INSERT ON ' + QUOTENAME(SchemaName) + N'.' + QUOTENAME(TableName) + N' TO ' + QUOTENAME(@user) + N';' + NCHAR(10)
    + N'DENY UPDATE, DELETE ON ' + QUOTENAME(SchemaName) + N'.' + QUOTENAME(TableName) + N' TO ' + QUOTENAME(@user) + N';' + NCHAR(10)
FROM #AuditTables;

SET @sql = @sql + N'DENY UPDATE, DELETE ON SCHEMA::[audit] TO ' + QUOTENAME(@user) + N';' + NCHAR(10);

EXEC sys.sp_executesql @sql;
GO

/* Batch 5: what the application user now holds on those tables. */
DECLARE @user sysname = (SELECT TOP (1) UserName FROM #AuditAppUser);
SELECT
    COALESCE(s.name + N'.' + o.name, N'SCHEMA::' + sc.name) AS Securable,
    p.state_desc AS [State],
    p.permission_name AS [Permission]
FROM sys.database_permissions AS p
LEFT JOIN sys.objects AS o ON p.class = 1 AND o.object_id = p.major_id
LEFT JOIN sys.schemas AS s ON s.schema_id = o.schema_id
LEFT JOIN sys.schemas AS sc ON p.class = 3 AND sc.schema_id = p.major_id
WHERE p.grantee_principal_id = DATABASE_PRINCIPAL_ID(@user)
  AND (EXISTS (SELECT 1 FROM #AuditTables AS a WHERE a.SchemaName = s.name AND a.TableName = o.name) OR sc.name = N'audit')
ORDER BY Securable, [Permission];

IF IS_ROLEMEMBER(N'db_owner', @user) = 1
    PRINT N'WARNING: the application user is a db_owner member; DENY does not bind it. Remove it from db_owner.';
GO

DROP TABLE IF EXISTS #AuditTables;
DROP TABLE IF EXISTS #AuditAppUser;
GO
