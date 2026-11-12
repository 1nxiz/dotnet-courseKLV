var builder = DistributedApplication.CreateBuilder(args);

// ============ Інфраструктура (Docker-контейнери, дані переживають перезапуск завдяки volume) ============

// SQL Server: бази LoansDb (Лаб. 01-02, ADO.NET/Dapper) і BooksDb (Лаб. 03, EF Core). Пароль — параметр-секрет.
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", sqlPassword)
                 .WithDataVolume("library-sql-data");
var loansDb = sql.AddDatabase("LoansDb");
var booksDb = sql.AddDatabase("BooksDb");

// MongoDB: база FeedbackDb (Лаб. 04)
var mongo = builder.AddMongoDB("mongo")
                   .WithDataVolume("library-mongo-data");
var feedbackDb = mongo.AddDatabase("FeedbackDb");

// ============ Мікросервіси ============
// Імена ресурсів БД збігаються з іменами рядків підключення в сервісах (ConnectionStrings__LoansDb тощо),
// тому код сервісів не змінюється. Імена ресурсів сервісів — це DNS-імена для service discovery.
// Порожні бази створюють самі сервіси: Loans — скриптами db/p1, Books — міграціями EF, Feedback — індексами й сідінгом.

var loansApi = builder.AddProject<Projects.Loans_Api>("loans-api")
                      .WithReference(loansDb)
                      .WaitFor(sql)
                      .WithHttpHealthCheck("/health");

var booksApi = builder.AddProject<Projects.Books_Api>("books-api")
                      .WithReference(booksDb)
                      .WaitFor(sql)
                      .WithHttpHealthCheck("/health");

var feedbackApi = builder.AddProject<Projects.Feedback_WebApi>("feedback-api")
                         .WithReference(feedbackDb)
                         .WaitFor(mongo)
                         .WithHttpHealthCheck("/health");

// ============ Aggregator: паралельно опитує три сервіси (service discovery за іменами ресурсів) ============
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
                        .WithReference(loansApi).WaitFor(loansApi)
                        .WithReference(booksApi).WaitFor(booksApi)
                        .WithReference(feedbackApi).WaitFor(feedbackApi)
                        .WithHttpHealthCheck("/health");

// ============ API Gateway (YARP): єдина точка входу для клієнтів — http://localhost:5000 ============
builder.AddProject<Projects.Platform_ApiGateway>("gateway")
       .WithReference(loansApi)
       .WithReference(booksApi)
       .WithReference(feedbackApi)
       .WithReference(aggregator)
       .WaitFor(aggregator)
       .WithEndpoint("http", e => e.Port = 5000)          // фіксований публічний порт Gateway
       .WithExternalHttpEndpoints()
       .WithHttpHealthCheck("/health");

builder.Build().Run();
