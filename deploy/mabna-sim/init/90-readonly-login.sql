-- Zharfa Mabna simulator: read-only login mirroring production rights (FR-03).
-- Generic, idempotent. Requires TotalSystem to exist. $(RO_PASSWORD) comes from
-- sqlcmd -v (MABNA_SIM_RO_PASSWORD in deploy/.env).
IF NOT EXISTS (SELECT 1 FROM sys.sql_logins WHERE name = N'zharfa_ro')
    CREATE LOGIN zharfa_ro WITH PASSWORD = N'$(RO_PASSWORD)', CHECK_POLICY = OFF;
GO
USE TotalSystem;
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'zharfa_ro')
    CREATE USER zharfa_ro FOR LOGIN zharfa_ro;
GO
ALTER ROLE db_datareader ADD MEMBER zharfa_ro;
DENY INSERT, UPDATE, DELETE, ALTER, EXECUTE TO zharfa_ro;
DENY CREATE TABLE, CREATE VIEW, CREATE PROCEDURE TO zharfa_ro;
GO
