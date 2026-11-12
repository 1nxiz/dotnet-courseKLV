IF OBJECT_ID(N'[__EFMigrationsHistory]') IS NULL
BEGIN
    CREATE TABLE [__EFMigrationsHistory] (
        [MigrationId] nvarchar(150) NOT NULL,
        [ProductVersion] nvarchar(32) NOT NULL,
        CONSTRAINT [PK___EFMigrationsHistory] PRIMARY KEY ([MigrationId])
    );
END;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE TABLE [Authors] (
        [Id] int NOT NULL IDENTITY,
        [FullName] nvarchar(200) NOT NULL,
        [Country] nvarchar(100) NULL,
        [BirthYear] smallint NULL,
        CONSTRAINT [PK_Authors] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Authors_BirthYear] CHECK ([BirthYear] BETWEEN 1000 AND 2100)
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE TABLE [Genres] (
        [Id] int NOT NULL,
        [Name] nvarchar(100) NOT NULL,
        [Slug] nvarchar(100) NOT NULL,
        CONSTRAINT [PK_Genres] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Genres_Slug] CHECK ([Slug] NOT LIKE '% %')
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE TABLE [Books] (
        [Id] int NOT NULL IDENTITY,
        [Title] nvarchar(300) NOT NULL,
        [Isbn] nvarchar(20) NOT NULL,
        [Price] decimal(18,2) NOT NULL,
        [TotalCopies] int NOT NULL,
        [PublishedYear] smallint NULL,
        [IsPublished] bit NOT NULL,
        [AuthorId] int NOT NULL,
        [CreatedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        [UpdatedAt] datetime2(3) NULL,
        [RowVersion] rowversion NOT NULL,
        CONSTRAINT [PK_Books] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_Books_Price] CHECK ([Price] > 0),
        CONSTRAINT [CK_Books_PublishedYear] CHECK ([PublishedYear] BETWEEN 1450 AND 2100),
        CONSTRAINT [CK_Books_TotalCopies] CHECK ([TotalCopies] >= 0),
        CONSTRAINT [FK_Books_Authors_AuthorId] FOREIGN KEY ([AuthorId]) REFERENCES [Authors] ([Id]) ON DELETE NO ACTION
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE TABLE [BookDetails] (
        [BookId] int NOT NULL,
        [Description] nvarchar(4000) NOT NULL,
        [Publisher] nvarchar(200) NULL,
        [Pages] int NULL,
        [Language] nvarchar(10) NOT NULL DEFAULT N'uk',
        CONSTRAINT [PK_BookDetails] PRIMARY KEY ([BookId]),
        CONSTRAINT [CK_BookDetails_Pages] CHECK ([Pages] > 0),
        CONSTRAINT [FK_BookDetails_Books_BookId] FOREIGN KEY ([BookId]) REFERENCES [Books] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE TABLE [BookGenres] (
        [BookId] int NOT NULL,
        [GenreId] int NOT NULL,
        [AddedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_BookGenres] PRIMARY KEY ([BookId], [GenreId]),
        CONSTRAINT [FK_BookGenres_Books_BookId] FOREIGN KEY ([BookId]) REFERENCES [Books] ([Id]) ON DELETE CASCADE,
        CONSTRAINT [FK_BookGenres_Genres_GenreId] FOREIGN KEY ([GenreId]) REFERENCES [Genres] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE TABLE [BookImages] (
        [Id] int NOT NULL IDENTITY,
        [BookId] int NOT NULL,
        [Url] nvarchar(500) NOT NULL,
        [SortOrder] int NOT NULL,
        CONSTRAINT [PK_BookImages] PRIMARY KEY ([Id]),
        CONSTRAINT [CK_BookImages_SortOrder] CHECK ([SortOrder] >= 0),
        CONSTRAINT [FK_BookImages_Books_BookId] FOREIGN KEY ([BookId]) REFERENCES [Books] ([Id]) ON DELETE CASCADE
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Slug') AND [object_id] = OBJECT_ID(N'[Genres]'))
        SET IDENTITY_INSERT [Genres] ON;
    EXEC(N'INSERT INTO [Genres] ([Id], [Name], [Slug])
    VALUES (1, N''Класика'', N''classic''),
    (2, N''Пригоди'', N''adventure''),
    (3, N''Роман'', N''novel''),
    (4, N''Програмування'', N''programming''),
    (5, N''Архітектура ПЗ'', N''software-architecture''),
    (6, N''Поезія'', N''poetry'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Id', N'Name', N'Slug') AND [object_id] = OBJECT_ID(N'[Genres]'))
        SET IDENTITY_INSERT [Genres] OFF;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Authors_FullName] ON [Authors] ([FullName]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_BookGenres_GenreId_AddedAt] ON [BookGenres] ([GenreId], [AddedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_BookImages_BookId_SortOrder] ON [BookImages] ([BookId], [SortOrder]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Books_AuthorId] ON [Books] ([AuthorId]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Books_Isbn] ON [Books] ([Isbn]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Books_Price] ON [Books] ([Price]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE INDEX [IX_Books_Title] ON [Books] ([Title]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Genres_Name] ON [Genres] ([Name]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    CREATE UNIQUE INDEX [IX_Genres_Slug] ON [Genres] ([Slug]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112114401_InitialCreate'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261112114401_InitialCreate', N'8.0.11');
END;
GO

COMMIT;
GO

BEGIN TRANSACTION;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112162225_AddIntegrationEvents'
)
BEGIN
    ALTER TABLE [Books] ADD [TimesLoaned] int NOT NULL DEFAULT 0;
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112162225_AddIntegrationEvents'
)
BEGIN
    CREATE TABLE [ProcessedEvents] (
        [EventId] uniqueidentifier NOT NULL,
        [EventType] nvarchar(200) NOT NULL,
        [ProcessedAt] datetime2(3) NOT NULL DEFAULT (SYSUTCDATETIME()),
        CONSTRAINT [PK_ProcessedEvents] PRIMARY KEY ([EventId])
    );
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112162225_AddIntegrationEvents'
)
BEGIN
    EXEC(N'ALTER TABLE [Books] ADD CONSTRAINT [CK_Books_TimesLoaned] CHECK ([TimesLoaned] >= 0)');
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112162225_AddIntegrationEvents'
)
BEGIN
    CREATE INDEX [IX_ProcessedEvents_ProcessedAt] ON [ProcessedEvents] ([ProcessedAt]);
END;
GO

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261112162225_AddIntegrationEvents'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261112162225_AddIntegrationEvents', N'8.0.11');
END;
GO

COMMIT;
GO

