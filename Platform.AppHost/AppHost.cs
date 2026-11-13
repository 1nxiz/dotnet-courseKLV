var builder = DistributedApplication.CreateBuilder(args);

// Варіант 1: існуюча БД з Лаб. 01 (рядок підключення ConnectionStrings:LoansDb в appsettings.json AppHost)
// var loansDb = builder.AddConnectionString("LoansDb");

// Варіант 2 (використано): SQL Server у Docker-контейнері; пароль — параметр-секрет AppHost.
// Ім'я ресурсу бази "LoansDb" збігається з ім'ям рядка підключення в Loans.Api,
// тому Aspire передає його автоматично (ConnectionStrings__LoansDb) і код API не змінюється.
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", sqlPassword)
                 .WithDataVolume("library-loans-sql-data");
var loansDb = sql.AddDatabase("LoansDb");

// Порожню базу в контейнері розгортає сам Loans.Api скриптами db/p1 (Database:InitializeOnStartup у Development)
builder.AddProject<Projects.Loans_Api>("loans-api")
       .WithReference(loansDb)
       .WaitFor(sql);

builder.Build().Run();
