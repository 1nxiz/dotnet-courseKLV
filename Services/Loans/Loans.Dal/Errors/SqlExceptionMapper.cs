using Loans.Domain.Exceptions;
using Microsoft.Data.SqlClient;

namespace Loans.Dal.Errors;

/// <summary>
/// Перетворює помилки SQL Server на доменні винятки, щоб «сирі» SQL-повідомлення не потрапляли в API.
/// Номери 5000x — керовані помилки (THROW) процедур з Лаб. 01.
/// </summary>
public static class SqlExceptionMapper
{
    public static bool TryMap(SqlException ex, out Exception mapped)
    {
        mapped = ex.Number switch
        {
            50001 => new NotFoundException(ex.Message, ex),
            50002 => new BusinessConflictException(ex.Message, ex),
            50003 => new ConcurrencyException(ex.Message, ex),
            50004 => new ValidationException(ex.Message, inner: ex),
            2601 or 2627 => new BusinessConflictException("Record with the same unique key already exists", ex),
            547 => new ValidationException("The data violates a database constraint (CHECK or FOREIGN KEY)", inner: ex),
            1205 => new BusinessConflictException("The operation was chosen as a deadlock victim. Retry the request", ex),
            _ => ex
        };
        return !ReferenceEquals(mapped, ex);
    }
}
