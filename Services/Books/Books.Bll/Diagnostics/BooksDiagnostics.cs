using System.Diagnostics;

namespace Books.Bll.Diagnostics;

/// <summary>Власні spans бізнес-операцій каталогу (ActivitySource = ApplicationName "Books.Api").</summary>
public static class BooksDiagnostics
{
    public const string SourceName = "Books.Api";
    public static readonly ActivitySource ActivitySource = new(SourceName);
}
