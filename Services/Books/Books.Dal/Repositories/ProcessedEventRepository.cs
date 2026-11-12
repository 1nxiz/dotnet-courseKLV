using Books.Dal.Entities;
using Microsoft.EntityFrameworkCore;

namespace Books.Dal.Repositories;

public class ProcessedEventRepository : IProcessedEventRepository
{
    private readonly AppDbContext _context;

    public ProcessedEventRepository(AppDbContext context) => _context = context;

    public Task<bool> ExistsAsync(Guid eventId, CancellationToken ct = default)
        => _context.ProcessedEvents.AsNoTracking().AnyAsync(e => e.EventId == eventId, ct);

    public void Add(Guid eventId, string eventType)
        => _context.ProcessedEvents.Add(new ProcessedEvent { EventId = eventId, EventType = eventType });
}
