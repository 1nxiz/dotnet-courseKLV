using Feedback.Domain.ValueObjects;
using MongoDB.Bson;
using MongoDB.Bson.Serialization;
using MongoDB.Bson.Serialization.Serializers;

namespace Feedback.Infrastructure.Persistence.Serializers;

/// <summary>
/// Власний BSON-серіалізатор value object Rating: у документі зберігається просто число (rating: 5),
/// як у схемі з Лаб. 01, а в домені — незмінний об'єкт із валідацією діапазону.
/// </summary>
public sealed class RatingSerializer : SerializerBase<Rating>
{
    public override Rating Deserialize(BsonDeserializationContext context, BsonDeserializationArgs args)
    {
        var reader = context.Reader;
        var value = reader.GetCurrentBsonType() switch
        {
            BsonType.Int32 => reader.ReadInt32(),
            BsonType.Int64 => (int)reader.ReadInt64(),
            BsonType.Double => (int)reader.ReadDouble(),
            var other => throw new FormatException($"Cannot deserialize Rating from BSON type {other}")
        };
        return new Rating(value);
    }

    public override void Serialize(BsonSerializationContext context, BsonSerializationArgs args, Rating value)
        => context.Writer.WriteInt32(value.Value);
}
