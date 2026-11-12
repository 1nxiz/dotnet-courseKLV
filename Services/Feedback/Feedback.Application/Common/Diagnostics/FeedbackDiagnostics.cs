using System.Diagnostics;

namespace Feedback.Application.Common.Diagnostics;

/// <summary>Власні spans команд/запитів CQRS (ActivitySource = ApplicationName "Feedback.WebApi").</summary>
public static class FeedbackDiagnostics
{
    public const string SourceName = "Feedback.WebApi";
    public static readonly ActivitySource ActivitySource = new(SourceName);
}
