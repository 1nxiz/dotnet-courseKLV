namespace Library.Loans.Grpc;

/// <summary>Перетворення decimal &lt;-&gt; DecimalValue (units + nanos) без втрати точності.</summary>
public partial class DecimalValue
{
    private const decimal NanoFactor = 1_000_000_000;

    public static implicit operator decimal(DecimalValue value) => value.Units + value.Nanos / NanoFactor;

    public static implicit operator DecimalValue(decimal value)
    {
        var units = decimal.ToInt64(value);
        return new DecimalValue { Units = units, Nanos = decimal.ToInt32((value - units) * NanoFactor) };
    }
}
