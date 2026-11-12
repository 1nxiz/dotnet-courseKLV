using RabbitMQ.Client;

namespace Platform.EventBus;

/// <summary>
/// Топологія платформи, оголошена в коді (декларативно) під час старту кожного сервісу — повторне оголошення безпечне.
/// <code>
/// books-events (topic) ──book.*──────────► loans.books-sync     ──(DLX)──► platform.dlx ─► loans.books-sync.dlq
///                      └─book.renamed────► feedback.books-sync  ──(DLX)──► platform.dlx ─► feedback.books-sync.dlq
/// loans-events (topic) ──loan.status_changed──► books.loans-sync ──(DLX)──► platform.dlx ─► books.loans-sync.dlq
/// </code>
/// DLX — один direct-exchange з routing key = ім'я черги: відхилене повідомлення потрапляє лише в DLQ своєї черги.
/// Усе durable; повідомлення публікуються persistent.
/// </summary>
public static class Topology
{
    public const string BooksExchange = "books-events";
    public const string LoansExchange = "loans-events";
    public const string DeadLetterExchange = "platform.dlx";

    public static class RoutingKeys
    {
        public const string BookCreated = "book.created";
        public const string BookPriceChanged = "book.price_changed";
        public const string BookRenamed = "book.renamed";
        public const string BookDeleted = "book.deleted";
        public const string LoanStatusChanged = "loan.status_changed";
    }

    public static class Queues
    {
        public const string LoansBooksSync = "loans.books-sync";          // копія довідника книг у LoansDb
        public const string FeedbackBooksSync = "feedback.books-sync";    // bookTitle у відгуках і обговореннях
        public const string BooksLoansSync = "books.loans-sync";          // лічильник видач книги в Каталозі
    }

    public static string DeadLetterQueue(string queue) => $"{queue}.dlq";

    private static readonly (string Queue, string Exchange, string[] Bindings)[] QueueBindings =
    [
        (Queues.LoansBooksSync, BooksExchange, ["book.*"]),
        (Queues.FeedbackBooksSync, BooksExchange, [RoutingKeys.BookRenamed]),
        (Queues.BooksLoansSync, LoansExchange, [RoutingKeys.LoanStatusChanged])
    ];

    public static async Task DeclareAsync(IChannel channel, CancellationToken ct = default)
    {
        await channel.ExchangeDeclareAsync(BooksExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(LoansExchange, ExchangeType.Topic, durable: true, autoDelete: false, cancellationToken: ct);
        await channel.ExchangeDeclareAsync(DeadLetterExchange, ExchangeType.Direct, durable: true, autoDelete: false, cancellationToken: ct);

        foreach (var (queue, exchange, bindings) in QueueBindings)
        {
            await channel.QueueDeclareAsync(queue, durable: true, exclusive: false, autoDelete: false,
                arguments: new Dictionary<string, object?>
                {
                    ["x-dead-letter-exchange"] = DeadLetterExchange,
                    ["x-dead-letter-routing-key"] = queue
                }, cancellationToken: ct);
            foreach (var key in bindings)
                await channel.QueueBindAsync(queue, exchange, key, cancellationToken: ct);

            var dlq = DeadLetterQueue(queue);
            await channel.QueueDeclareAsync(dlq, durable: true, exclusive: false, autoDelete: false, cancellationToken: ct);
            await channel.QueueBindAsync(dlq, DeadLetterExchange, queue, cancellationToken: ct);
        }
    }
}
