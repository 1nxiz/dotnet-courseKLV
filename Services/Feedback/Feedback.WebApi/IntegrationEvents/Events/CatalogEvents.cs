using Platform.EventBus;

namespace Feedback.WebApi.IntegrationEvents.Events;

/// <summary>
/// Подія Каталогу, яку споживає сервіс відгуків (черга feedback.books-sync, routing key book.renamed).
/// Ім'я типу й поля збігаються з контрактом Books.Api/IntegrationEvents — тип передається в BasicProperties.Type.
/// </summary>
public sealed record BookRenamedEvent(int BookId, string OldTitle, string NewTitle) : IntegrationEvent;
