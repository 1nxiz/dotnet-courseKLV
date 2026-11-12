-- =====================================================================================================
-- Міграція 001 (Лаб. 08): журнал оброблених integration events для ідемпотентних споживачів.
-- Застосовується SqlMigrator під час старту loans-api (у будь-якому середовищі), один раз (журнал dbo.SchemaMigrations).
-- Скрипт ідемпотентний: повторний запуск нічого не змінює.
-- =====================================================================================================
IF OBJECT_ID(N'dbo.ProcessedEvents', N'U') IS NULL
BEGIN
    CREATE TABLE dbo.ProcessedEvents (
        EventId     UNIQUEIDENTIFIER NOT NULL CONSTRAINT PK_ProcessedEvents PRIMARY KEY,   -- ключ ідемпотентності
        EventType   NVARCHAR(200)    NOT NULL,
        ProcessedAt DATETIME2(3)     NOT NULL CONSTRAINT DF_ProcessedEvents_ProcessedAt DEFAULT SYSUTCDATETIME()
    );
    -- прибирання старих записів (DELETE ... WHERE ProcessedAt < ...) без сканування таблиці
    CREATE INDEX IX_ProcessedEvents_ProcessedAt ON dbo.ProcessedEvents (ProcessedAt);
END
GO
