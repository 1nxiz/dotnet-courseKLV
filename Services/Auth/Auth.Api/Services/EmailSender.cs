using System.Collections.Concurrent;

namespace Auth.Api.Services;

public interface IEmailSender
{
    Task SendAsync(string to, string subject, string body, CancellationToken ct);
}

/// <summary>
/// Mock email service для розробки: лист пишеться в лог і зберігається в пам'яті (останні листи кожного адресата).
/// У Development їх видно через GET /auth/dev/mailbox/{email} — так тестові сценарії отримують токени підтвердження
/// email і скидання пароля. У production замінюється реальним SMTP / SendGrid.
/// </summary>
public sealed class DevEmailSender(ILogger<DevEmailSender> logger) : IEmailSender
{
    private readonly ConcurrentDictionary<string, ConcurrentQueue<DevEmail>> _mailbox = new(StringComparer.OrdinalIgnoreCase);

    public Task SendAsync(string to, string subject, string body, CancellationToken ct)
    {
        var queue = _mailbox.GetOrAdd(to, _ => new ConcurrentQueue<DevEmail>());
        queue.Enqueue(new DevEmail(to, subject, body, DateTime.UtcNow));
        while (queue.Count > 20) queue.TryDequeue(out _);
        logger.LogInformation("[DEV EMAIL] To {To}: {Subject}", to, subject);
        return Task.CompletedTask;
    }

    public IReadOnlyList<DevEmail> Read(string email)
        => _mailbox.TryGetValue(email, out var queue) ? queue.Reverse().ToList() : [];
}

public sealed record DevEmail(string To, string Subject, string Body, DateTime SentAt);
