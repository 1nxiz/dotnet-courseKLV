using Feedback.Domain.Entities;
using Feedback.Domain.ValueObjects;
using Feedback.Infrastructure.Persistence;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;

namespace Feedback.Tests;

/// <summary>BsonClassMap дає документ зі структурою Лаб. 01 (camelCase, вкладені author/comments, rating — число)
/// без витоку приватних полів .NET.</summary>
public class BsonMappingTests
{
    public BsonMappingTests() => MongoMappings.Register();

    [Fact]
    public void Review_is_serialized_with_lab01_document_shape()
    {
        var review = new Review(4, "Clean Architecture", new Author("u-olena", "Олена"), new Rating(5), "Текст",
            photos: ["https://x/1.jpg"]);
        review.AddComment(new Author("u-2", "Андрій"), "Згоден");

        var doc = review.ToBsonDocument();

        Assert.True(doc.Contains("_id"));
        Assert.Equal(4L, doc["bookId"].AsInt64);
        Assert.Equal("Олена", doc["author"]["displayName"].AsString);
        Assert.Equal(BsonType.Int32, doc["rating"].BsonType);
        Assert.Equal(1, doc["comments"].AsBsonArray.Count);
        Assert.Equal("u-2", doc["comments"][0]["author"]["userId"].AsString);
        Assert.False(doc.Contains("readingProgress"));            // null не записується
        Assert.DoesNotContain(doc.Names, n => n.StartsWith('_') && n != "_id");
        Assert.DoesNotContain(doc.Names, n => char.IsUpper(n[0]));
    }

    [Fact]
    public void Document_with_unknown_fields_round_trips_without_data_loss()
    {
        var original = BsonDocument.Parse("""
            { "_id": ObjectId("66a000000000000000000003"), "bookId": NumberLong(6), "bookTitle": "DDIA",
              "author": { "userId": "u-iryna", "displayName": "Ірина" }, "rating": 5, "text": "Найкраща",
              "quote": "Data outlives code.", "comments": [], "createdAt": ISODate("2026-10-05T19:00:00Z"), "updatedAt": null, "version": 1 }
            """);

        var review = BsonSerializer.Deserialize<Review>(original);
        var again = review.ToBsonDocument();

        Assert.Equal(5, review.Rating.Value);
        Assert.Equal("Data outlives code.", again["quote"].AsString);     // гнучка схема збережена
        Assert.Equal(1, review.Version);
    }
}
