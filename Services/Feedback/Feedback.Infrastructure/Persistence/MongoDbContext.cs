using Feedback.Domain.Entities;
using Microsoft.Extensions.Options;
using MongoDB.Driver;

namespace Feedback.Infrastructure.Persistence;

/// <summary>Налаштування підключення (секція "MongoDb" в appsettings.{Environment}.json).</summary>
public class MongoDbOptions
{
    public const string SectionName = "MongoDb";

    /// <summary>Рядок підключення; пул і таймаути задаються параметрами (maxPoolSize, serverSelectionTimeoutMS).</summary>
    public string ConnectionString { get; set; } = "";
    public string DatabaseName { get; set; } = "";
}

/// <summary>Контекст MongoDB: IMongoDatabase за налаштуваннями (IOptions) і колекції для доменних сутностей.</summary>
public class MongoDbContext
{
    public const string ReviewsCollection = "reviews";
    public const string DiscussionsCollection = "discussions";
    public const string MessagesCollection = "messages";

    public MongoDbContext(IMongoClient client, IOptions<MongoDbOptions> options)
    {
        Client = client;
        Database = client.GetDatabase(options.Value.DatabaseName);
    }

    public IMongoClient Client { get; }
    public IMongoDatabase Database { get; }

    public IMongoCollection<Review> Reviews => Database.GetCollection<Review>(ReviewsCollection);
    public IMongoCollection<Discussion> Discussions => Database.GetCollection<Discussion>(DiscussionsCollection);
    public IMongoCollection<Message> Messages => Database.GetCollection<Message>(MessagesCollection);
}
