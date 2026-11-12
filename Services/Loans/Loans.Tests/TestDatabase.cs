namespace Loans.Tests;

/// <summary>Інтеграційні тести працюють проти реальної LoansDb (розгорнутої скриптами db/p1).</summary>
public static class TestDatabase
{
    public static string? ConnectionString => Environment.GetEnvironmentVariable("ConnectionStrings__LoansDb");

    public static void SkipIfUnavailable() =>
        Skip.If(string.IsNullOrWhiteSpace(ConnectionString),
            "Set ConnectionStrings__LoansDb to run integration tests against a real LoansDb");
}
