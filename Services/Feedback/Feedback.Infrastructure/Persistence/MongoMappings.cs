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

    private static System.Reflection.ConstructorInfo PrivateDefaultConstructor<T>()
        => typeof(T).GetConstructor(System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic,
               binder: null, Type.EmptyTypes, modifiers: null)
           ?? throw new InvalidOperationException($"{typeof(T).Name} must have a private parameterless constructor");

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
                cm.SetIgnoreExtraElements(true);
                cm.MapIdMember(e => e.Id);
                cm.MapProperty(e => e.CreatedAt).SetElementName("createdAt");
                cm.MapProperty(e => e.UpdatedAt).SetElementName("updatedAt");
                cm.MapProperty(e => e.Version).SetElementName("version");
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
            // Сутності мапляться ЯВНО (без AutoMap): приватні сетери інакше змушують драйвер вважати клас «незмінним»
            // і шукати конструктор із параметрами. Створення — через приватний конструктор без параметрів.
            BsonClassMap.TryRegisterClassMap<Review>(cm =>
            {
                cm.MapConstructor(PrivateDefaultConstructor<Review>());
                cm.MapProperty(r => r.BookId).SetElementName("bookId");
                cm.MapProperty(r => r.BookTitle).SetElementName("bookTitle");
                cm.MapProperty(r => r.Author).SetElementName("author");
                cm.MapProperty(r => r.Rating).SetElementName("rating");                // RatingSerializer -> число
                cm.MapProperty(r => r.Text).SetElementName("text");
                cm.MapProperty(r => r.Photos).SetElementName("photos").SetIgnoreIfNull(true);   // поле є лише в частини документів
                cm.MapProperty(r => r.ReadingProgress).SetElementName("readingProgress").SetIgnoreIfNull(true);
                cm.MapProperty(r => r.DiscussionId).SetElementName("discussionId");
                cm.MapField("_comments").SetElementName("comments");                    // приватне поле -> вбудований масив
                cm.MapExtraElementsField("_extraElements");                              // гнучка схема: невідомі поля не губляться
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
                cm.MapConstructor(PrivateDefaultConstructor<Discussion>());
                cm.SetIgnoreExtraElements(true);
                cm.MapProperty(d => d.BookId).SetElementName("bookId");
                cm.MapProperty(d => d.BookTitle).SetElementName("bookTitle");
                cm.MapProperty(d => d.Title).SetElementName("title");
                cm.MapProperty(d => d.StartedBy).SetElementName("startedBy");
                cm.MapProperty(d => d.MessagesCount).SetElementName("messagesCount");
                cm.MapProperty(d => d.LastActivityAt).SetElementName("lastActivityAt");
                cm.MapField("_tags").SetElementName("tags");
                cm.MapField("_lastMessages").SetElementName("lastMessages");
            });
            BsonClassMap.TryRegisterClassMap<Message>(cm =>
            {
                cm.MapConstructor(PrivateDefaultConstructor<Message>());
                cm.SetIgnoreExtraElements(true);                                         // reactions тощо
                cm.MapProperty(m => m.DiscussionId).SetElementName("discussionId");
                cm.MapProperty(m => m.Author).SetElementName("author");
                cm.MapProperty(m => m.Text).SetElementName("text");
                cm.MapProperty(m => m.ReplyToId).SetElementName("replyToId");
                cm.MapProperty(m => m.EditedAt).SetElementName("editedAt");
            });

            _registered = true;
        }
    }
}
