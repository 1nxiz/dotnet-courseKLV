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

// Redis (Лаб. 06): L2-кеш сервісів і кеш складених відповідей Aggregator. Рядок підключення ConnectionStrings:redis
// Aspire передає всім, хто має WithReference(redis).
var redis = builder.AddRedis("redis")
                   .WithDataVolume("library-redis-data");

// ============ Мікросервіси ============
// Імена ресурсів БД збігаються з іменами рядків підключення в сервісах (ConnectionStrings__LoansDb тощо),
// тому код сервісів не змінюється. Імена ресурсів сервісів — це DNS-імена для service discovery.
// Порожні бази створюють самі сервіси: Loans — скриптами db/p1, Books — міграціями EF, Feedback — індексами й сідінгом.

var loansApi = builder.AddProject<Projects.Loans_Api>("loans-api")
                      .WithReference(loansDb)
                      .WaitFor(sql)
                      .WithReference(redis).WaitFor(redis)
                      .WithHttpHealthCheck("/health");

var booksApi = builder.AddProject<Projects.Books_Api>("books-api")
                      .WithReference(booksDb)
                      .WaitFor(sql)
                      .WithReference(redis).WaitFor(redis)
                      .WithHttpHealthCheck("/health");

var feedbackApi = builder.AddProject<Projects.Feedback_WebApi>("feedback-api")
                         .WithReference(feedbackDb)
                         .WaitFor(mongo)
                         .WithReference(redis).WaitFor(redis)
                         .WithHttpHealthCheck("/health");

// ============ Aggregator: паралельно опитує три сервіси через gRPC (endpoint "grpc" кожного сервісу) ============
// Ендпоінти сервісів "http" (REST) і "grpc" (HTTP/2) Aspire бере з Kestrel:Endpoints у appsettings.json сервісів.
var aggregator = builder.AddProject<Projects.Platform_Aggregator>("aggregator")
                        .WithReference(loansApi).WaitFor(loansApi)
                        .WithReference(booksApi).WaitFor(booksApi)
                        .WithReference(feedbackApi).WaitFor(feedbackApi)
                        .WithReference(redis).WaitFor(redis)
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
