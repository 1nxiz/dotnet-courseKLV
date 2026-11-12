namespace Books.Bll.Caching;

/// <summary>
/// Передбачувана схема ключів "books:{сутність}:{параметри}" — префікс сервісу ізолює ключі в спільному Redis,
/// а з id сутності завжди можна обчислити, що інвалідувати після запису.
/// </summary>
public static class CacheKeys
{
    public const string Prefix = "books:";
    public static string Book(int id) => $"{Prefix}book:{id}";
    public static string PublishedByAuthor(int authorId) => $"{Prefix}author:{authorId}:published";
    public const string Genres = $"{Prefix}genres:all";
    public const string Authors = $"{Prefix}authors:all";
}

/// <summary>Політика кешування для типу даних: L1 (пам'ять) завжди живе коротше, ніж L2 (Redis).</summary>
public sealed record CachePolicy(TimeSpan L1Sliding, TimeSpan L1Absolute, TimeSpan L2Absolute, CacheItemPriorityLevel Priority = CacheItemPriorityLevel.Normal)
{
    /// <summary>Картка книги: часто читається, змінюється рідко -> sliding у пам'яті, довший TTL у Redis.</summary>
    public static readonly CachePolicy BookCard = new(TimeSpan.FromMinutes(2), TimeSpan.FromMinutes(5), TimeSpan.FromMinutes(30), CacheItemPriorityLevel.High);

    /// <summary>Довідники (жанри задані міграцією, автори майже не змінюються) -> absolute expiration.</summary>
    public static readonly CachePolicy Reference = new(TimeSpan.FromMinutes(10), TimeSpan.FromMinutes(10), TimeSpan.FromHours(1), CacheItemPriorityLevel.NeverRemove);

    /// <summary>Списки, що залежать від кількох книг — коротко.</summary>
    public static readonly CachePolicy List = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), CacheItemPriorityLevel.Low);
}

/// <summary>Пріоритет витіснення з L1 (дзеркало CacheItemPriority, щоб ключі політик не залежали від реалізації).</summary>
public enum CacheItemPriorityLevel { Low, Normal, High, NeverRemove }
