-- Codelab.Security SQL Server stores (EF-less, idempotent: safe to run on every deployment).
-- Tables: replay nonces (request proofs, service assertions, one-time tickets), per-user session versions and revoked
-- session ids. Each table is created only when missing; nothing is ever dropped or altered here.
-- Purging expired rows is the host's job: resolve IEnumerable<IExpiredEntryPurger> from a scheduled job.

IF OBJECT_ID(N'dbo.SecurityReplayNonce', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SecurityReplayNonce
    (
        NonceHash     BINARY(32)   NOT NULL CONSTRAINT PK_SecurityReplayNonce PRIMARY KEY,
        ExpiresAtUtc  DATETIME2(3) NOT NULL,
        ConsumedAtUtc DATETIME2(3) NOT NULL
    );
    CREATE INDEX IX_SecurityReplayNonce_ExpiresAtUtc ON dbo.SecurityReplayNonce (ExpiresAtUtc);
END;

IF OBJECT_ID(N'dbo.SecuritySessionVersion', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SecuritySessionVersion
    (
        UserKey      NVARCHAR(128) NOT NULL CONSTRAINT PK_SecuritySessionVersion PRIMARY KEY,
        Version      BIGINT        NOT NULL,
        UpdatedAtUtc DATETIME2(3)  NOT NULL
    );
END;

IF OBJECT_ID(N'dbo.SecurityRevokedSession', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.SecurityRevokedSession
    (
        SessionId    NVARCHAR(128) NOT NULL CONSTRAINT PK_SecurityRevokedSession PRIMARY KEY,
        ExpiresAtUtc DATETIME2(3)  NOT NULL,
        RevokedAtUtc DATETIME2(3)  NOT NULL
    );
    CREATE INDEX IX_SecurityRevokedSession_ExpiresAtUtc ON dbo.SecurityRevokedSession (ExpiresAtUtc);
END;
