var builder = DistributedApplication.CreateBuilder(args);

// ============ Інфраструктура (Docker-контейнери, дані переживають перезапуск завдяки volume) ============

// SQL Server: LoansDb (Лаб. 01-02, ADO.NET/Dapper), BooksDb (Лаб. 03, EF Core), IdentityDb (Лаб. 09, Auth Service).
var sqlPassword = builder.AddParameter("sql-password", secret: true);
var sql = builder.AddSqlServer("sql", sqlPassword)
                 .WithDataVolume("library-sql-data");
var loansDb = sql.AddDatabase("LoansDb");
var booksDb = sql.AddDatabase("BooksDb");
var identityDb = sql.AddDatabase("IdentityDb");

// MongoDB: база FeedbackDb (Лаб. 04)
var mongo = builder.AddMongoDB("mongo")
                   .WithDataVolume("library-mongo-data");
var feedbackDb = mongo.AddDatabase("FeedbackDb");

// Redis (Лаб. 06): L2-кеш сервісів і кеш складених відповідей Aggregator.
var redis = builder.AddRedis("redis")
                   .WithDataVolume("library-redis-data");

// RabbitMQ (Лаб. 08): брокер подій; volume — повідомлення переживають перезапуск, Management UI — моніторинг черг.
var rabbit = builder.AddRabbitMQ("messaging")
                    .WithDataVolume("library-rabbitmq-data")
                    .WithManagementPlugin();

// ============ Identity Providers (Лаб. 09) ============

// Duende IdentityServer — ОКРЕМИЙ SQL Server і база IdentityServerDb (ізоляція безпеки від бізнес-баз)
var identityServerDb = builder.AddSqlServer("identityserver-sql", sqlPassword)
                              .WithDataVolume("library-identityserver-sql-data")
                              .AddDatabase("IdentityServerDb");
var identityServer = builder.AddProject<Projects.Platform_IdentityServer>("identityserver")
                            .WithReference(identityServerDb).WaitFor(identityServerDb)
                            .WithEndpoint("http", e => e.Port = 5010)   // фіксована адреса: Authority для сервісів і Postman
                            .WithExternalHttpEndpoints()
                            .WithHttpHealthCheck("/health");
var identityServerUrl = identityServer.GetEndpoint("http");

// Keycloak (траєкторія Б — Aspire-інтеграція): realm, clients, ролі, користувачі створює Pulumi (infra/keycloak)
var keycloakAdmin = builder.AddParameter("keycloak-admin");
var keycloakPassword = builder.AddParameter("keycloak-password", secret: true);
var keycloak = builder.AddKeycloak("keycloak", 8080, keycloakAdmin, keycloakPassword)
                      .WithDataVolume("library-keycloak-data");

// Auth Service — ASP.NET Identity + JWT на окремій базі IdentityDb
var authApi = builder.AddProject<Projects.Auth_Api>("auth-api")
                     .WithReference(identityDb).WaitFor(sql)
                     .WithHttpHealthCheck("/health");

// ============ Мікросервіси ============
// Кожен сервіс приймає токени трьох провайдерів: Auth Service (спільний ключ у Jwt), IdentityServer (Authority),
// Keycloak (service discovery ресурсу keycloak). Імена ресурсів сервісів — DNS-імена для service discovery.

IResourceBuilder<ProjectResource> WithIdentity(IResourceBuilder<ProjectResource> project) => project
    .WithEnvironment("IdentityServer__Authority", identityServerUrl)
    .WithReference(keycloak).WaitFor(identityServer);

var loansApi = WithIdentity(builder.AddProject<Projects.Loans_Api>("loans-api")
                      .WithReference(loansDb)
                      .WaitFor(sql)
                      .WithReference(redis).WaitFor(redis)
                      .WithReference(rabbit).WaitFor(rabbit)                    // споживає book.*, публікує loan.status_changed
                      .WithEnvironment("Database__InitializeOnStartup", "true") // Лаб. 10: і в кластері (Production) LoansDb створюється скриптами Лаб. 01
                      .WithHttpHealthCheck("/health"));

var booksApi = WithIdentity(builder.AddProject<Projects.Books_Api>("books-api")
                      .WithReference(booksDb)
                      .WaitFor(sql)
                      .WithReference(redis).WaitFor(redis)
                      .WithReference(rabbit).WaitFor(rabbit)                    // публікує book.*, споживає loan.status_changed
                      .WithEnvironment("Database__Seed", "true")                // Лаб. 10: демо-каталог і в кластері (Production)
                      .WithHttpHealthCheck("/health"));

var feedbackApi = WithIdentity(builder.AddProject<Projects.Feedback_WebApi>("feedback-api")
                         .WithReference(feedbackDb)
                         .WaitFor(mongo)
                         .WithReference(redis).WaitFor(redis)
                         .WithReference(rabbit).WaitFor(rabbit)                 // споживає book.renamed
                         .WithHttpHealthCheck("/health"));

var paymentsApi = WithIdentity(builder.AddProject<Projects.Payments_Api>("payments-api")
                         .WithHttpHealthCheck("/health"));

WithIdentity(authApi);

// ============ Aggregator: gRPC до трьох сервісів з токеном Client Credentials (IdentityServer) ============
var aggregator = WithIdentity(builder.AddProject<Projects.Platform_Aggregator>("aggregator")
                        .WithReference(loansApi).WaitFor(loansApi)
                        .WithReference(booksApi).WaitFor(booksApi)
                        .WithReference(feedbackApi).WaitFor(feedbackApi)
                        .WithReference(redis).WaitFor(redis)
                        .WithHttpHealthCheck("/health"));

// ============ API Gateway (YARP): єдина точка входу — http://localhost:5000; JWT валідується перед проксіюванням ============
WithIdentity(builder.AddProject<Projects.Platform_ApiGateway>("gateway")
       .WithReference(loansApi)
       .WithReference(booksApi)
       .WithReference(feedbackApi)
       .WithReference(paymentsApi)
       .WithReference(authApi)
       .WithReference(aggregator)
       .WaitFor(aggregator)
       .WithEndpoint("http", e => e.Port = 5000)          // фіксований публічний порт Gateway
       .WithExternalHttpEndpoints()
       .WithHttpHealthCheck("/health"));

// Лаб. 10 (aspirate): у режимі публікації маніфесту Aspire додає кожному проєкту ще й https-endpoint, який у контейнері
// не слухається (і для books-api/loans-api отримує той самий порт, що gRPC -> дубль порту в Kubernetes Service).
// TLS у кластері завершується на Ingress, тож для маніфестів лишаємо тільки http і grpc.
if (builder.ExecutionContext.IsPublishMode)
    foreach (var project in builder.Resources.OfType<ProjectResource>())
        foreach (var https in project.Annotations.OfType<EndpointAnnotation>().Where(e => e.UriScheme == "https").ToList())
            project.Annotations.Remove(https);

builder.Build().Run();
