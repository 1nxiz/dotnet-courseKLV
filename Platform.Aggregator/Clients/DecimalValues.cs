// Перетворення DecimalValue (units + nanos) -> decimal для клієнтських класів, згенерованих з .proto сервісів.
namespace Library.Loans.Grpc
{
    public partial class DecimalValue
    {
        public static implicit operator decimal(DecimalValue? value) => value is null ? 0m : value.Units + value.Nanos / 1_000_000_000m;
    }
}

namespace Library.Books.Grpc
{
    public partial class DecimalValue
    {
        public static implicit operator decimal(DecimalValue? value) => value is null ? 0m : value.Units + value.Nanos / 1_000_000_000m;
    }
}
