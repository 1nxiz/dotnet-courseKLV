using System.Diagnostics;

namespace Loans.Bll.Diagnostics;

/// <summary>
/// Власні spans бізнес-операцій (Лаб. 05). Ім'я ActivitySource = ApplicationName сервісу ("Loans.Api"),
/// тому ServiceDefaults підписує OpenTelemetry на нього без додаткового налаштування.
/// </summary>
public static class LoansDiagnostics
{
    public const string SourceName = "Loans.Api";
    public static readonly ActivitySource ActivitySource = new(SourceName);
}
