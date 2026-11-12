namespace Feedback.Application.Caching;

/// <summary>Ключі кешу сервісу відгуків (префікс "feedback:" ізолює їх у спільному Redis).</summary>
public static class CacheKeys
{
    public const string Prefix = "feedback:";
    public static string Review(string id) => $"{Prefix}review:{id}";
    public static string BookSummary(long bookId) => $"{Prefix}book:{bookId}:summary";
}

/// <summary>Політика кешування: L1 (пам'ять) коротший за L2 (Redis).</summary>
public sealed record CachePolicy(TimeSpan L1Sliding, TimeSpan L1Absolute, TimeSpan L2Absolute, CacheItemPriorityLevel Priority = CacheItemPriorityLevel.Normal)
{
    /// <summary>Повний відгук — змінюється рідко (редагування, коментарі), читається часто.</summary>
    public static readonly CachePolicy Review = new(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(10));

    /// <summary>Підсумок відгуків книги (середня оцінка + останні) — read-heavy для Aggregator.</summary>
    public static readonly CachePolicy BookSummary = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), CacheItemPriorityLevel.High);
}

/// <summary>Пріоритет витіснення з L1.</summary>
public enum CacheItemPriorityLevel { Low, Normal, High, NeverRemove }
