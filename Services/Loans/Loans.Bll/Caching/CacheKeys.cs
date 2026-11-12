namespace Loans.Bll.Caching;

/// <summary>Ключі кешу сервісу видач: префікс "loans:" ізолює їх у спільному Redis.</summary>
public static class CacheKeys
{
    public const string Prefix = "loans:";
    public static string Loan(long id) => $"{Prefix}loan:{id}";
    public static string Reader(long id) => $"{Prefix}reader:{id}";
}

/// <summary>Політика кешування: L1 (пам'ять) завжди коротший за L2 (Redis).</summary>
public sealed record CachePolicy(TimeSpan L1Sliding, TimeSpan L1Absolute, TimeSpan L2Absolute, CacheItemPriorityLevel Priority = CacheItemPriorityLevel.Normal)
{
    /// <summary>
    /// Картка видачі змінює статус кілька разів за життя, тому TTL короткі: L1 15 с обмежує вікно, коли інша
    /// репліка ще бачить старий стан; L2 інвалідується синхронно з кожним записом.
    /// </summary>
    public static readonly CachePolicy LoanCard = new(TimeSpan.FromSeconds(10), TimeSpan.FromSeconds(15), TimeSpan.FromMinutes(2));

    /// <summary>Картка читача містить персональні дані — короткий TTL, без довгого зберігання в Redis.</summary>
    public static readonly CachePolicy Reader = new(TimeSpan.FromSeconds(30), TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(5), CacheItemPriorityLevel.Low);
}

/// <summary>Пріоритет витіснення з L1.</summary>
public enum CacheItemPriorityLevel { Low, Normal, High, NeverRemove }
