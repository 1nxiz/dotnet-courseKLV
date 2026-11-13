/* =====================================================================
   Принцип найменших привілеїв: окремий логін для кожного сервісу з доступом
   ЛИШЕ до своєї бази. Застосунок «Видачі» не бачить BooksDb і навпаки.
   Паролі передаються змінними sqlcmd (-v LoansAppPassword=... -v BooksAppPassword=...).
   Скрипт ідемпотентний.
   ===================================================================== */
USE master;
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'loans_app')
    CREATE LOGIN loans_app WITH PASSWORD = N'$(LoansAppPassword)', CHECK_POLICY = ON, DEFAULT_DATABASE = LoansDb;
GO
IF NOT EXISTS (SELECT 1 FROM sys.server_principals WHERE name = N'books_app')
    CREATE LOGIN books_app WITH PASSWORD = N'$(BooksAppPassword)', CHECK_POLICY = ON, DEFAULT_DATABASE = BooksDb;
GO

USE LoansDb;
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'loans_app')
    CREATE USER loans_app FOR LOGIN loans_app;
GO
-- читання/запис даних і виконання процедур; без права змінювати схему
ALTER ROLE db_datareader ADD MEMBER loans_app;
ALTER ROLE db_datawriter ADD MEMBER loans_app;
GRANT EXECUTE ON SCHEMA::dbo TO loans_app;
GRANT EXECUTE ON TYPE::dbo.LoanItemList TO loans_app;
GO

USE BooksDb;
GO
IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE name = N'books_app')
    CREATE USER books_app FOR LOGIN books_app;
GO
ALTER ROLE db_datareader ADD MEMBER books_app;
ALTER ROLE db_datawriter ADD MEMBER books_app;
GO
PRINT 'Application logins configured (loans_app -> LoansDb, books_app -> BooksDb)';
GO
