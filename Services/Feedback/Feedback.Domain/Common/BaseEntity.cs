using MongoDB.Bson;

namespace Feedback.Domain.Common;

/// <summary>Базова сутність для MongoDB: Id (ObjectId), аудит і Version для оптимістичної конкурентності.</summary>
public abstract class BaseEntity
{
    public ObjectId Id { get; protected set; } = ObjectId.GenerateNewId();
    public DateTime CreatedAt { get; protected set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; protected set; }
    public int Version { get; protected set; }

    protected void Touch() => UpdatedAt = DateTime.UtcNow;

    /// <summary>Збільшує версію документа перед збереженням (викликає репозиторій).</summary>
    public void BumpVersion() => Version++;
}
