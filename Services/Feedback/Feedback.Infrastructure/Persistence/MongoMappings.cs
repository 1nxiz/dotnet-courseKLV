using Feedback.Domain.Common;
using Feedback.Domain.Entities;
using Feedback.Domain.ValueObjects;
using Feedback.Infrastructure.Persistence.Serializers;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Conventions;

namespace Feedback.Infrastructure.Persistence;

/// <summary>
/// BSON-мапінг доменних класів без атрибутів у домені (BsonClassMap). Реєструється ОДИН раз під час старту,
/// до першого звернення до БД. Конвенція camelCase — як у документах з Лаб. 01.
/// </summary>
public static class MongoMappings
{
    private static readonly object Sync = new();
    private static bool _registered;

    public static void Register()
    {
        lock (Sync)
        {
            if (_registered) return;

            ConventionRegistry.Register("feedback-conventions", new ConventionPack
            {
                new CamelCaseElementNameConvention(),
                new IgnoreExtraElementsConvention(true)
            }, type => type.Namespace?.StartsWith("Feedback.Domain", StringComparison.Ordinal) == true);

            BsonSerializer.TryRegisterSerializer(new RatingSerializer());

            BsonClassMap.TryRegisterClassMap<BaseEntity>(cm =>
            {
                cm.AutoMap();
                cm.MapIdMember(e => e.Id);
                cm.MapMember(e => e.UpdatedAt).SetElementName("updatedAt");
            });

            // ---------- value objects: незмінні, створюються через конструктор ----------
            BsonClassMap.TryRegisterClassMap<Author>(cm =>
            {
                cm.MapProperty(a => a.UserId).SetElementName("userId");
                cm.MapProperty(a => a.DisplayName).SetElementName("displayName");
                cm.MapCreator(a => new Author(a.UserId, a.DisplayName));
            });
            BsonClassMap.TryRegisterClassMap<ReadingProgress>(cm =>
            {
                cm.MapProperty(p => p.PagesRead).SetElementName("pagesRead");
                cm.MapProperty(p => p.Finished).SetElementName("finished");
                cm.MapCreator(p => new ReadingProgress(p.PagesRead, p.Finished));
            });

            // ---------- reviews ----------
            BsonClassMap.TryRegisterClassMap<Comment>(cm =>
            {
                cm.MapProperty(c => c.Author).SetElementName("author");
                cm.MapProperty(c => c.Text).SetElementName("text");
                cm.MapProperty(c => c.CreatedAt).SetElementName("createdAt");
                cm.MapCreator(c => new Comment(c.Author, c.Text, c.CreatedAt));
            });
            BsonClassMap.TryRegisterClassMap<Review>(cm =>
            {
                cm.AutoMap();
                cm.MapField("_comments").SetElementName("comments");          // приватне поле -> вбудований масив
                cm.MapExtraElementsField("_extraElements");                     // гнучка схема: невідомі поля не губляться
                cm.GetMemberMap(r => r.Photos).SetIgnoreIfNull(true);           // поле є лише в частини документів
                cm.GetMemberMap(r => r.ReadingProgress).SetIgnoreIfNull(true);
            });

            // ---------- discussions / messages ----------
            BsonClassMap.TryRegisterClassMap<MessagePreview>(cm =>
            {
                cm.MapProperty(m => m.MessageId).SetElementName("messageId");
                cm.MapProperty(m => m.Author).SetElementName("author");
                cm.MapProperty(m => m.Text).SetElementName("text");
                cm.MapProperty(m => m.CreatedAt).SetElementName("createdAt");
                cm.MapCreator(m => new MessagePreview(m.MessageId, m.Author, m.Text, m.CreatedAt));
            });
            BsonClassMap.TryRegisterClassMap<Discussion>(cm =>
            {
                cm.AutoMap();
                cm.MapField("_tags").SetElementName("tags");
                cm.MapField("_lastMessages").SetElementName("lastMessages");
            });
            BsonClassMap.TryRegisterClassMap<Message>(cm => cm.AutoMap());

            _registered = true;
        }
    }
}
