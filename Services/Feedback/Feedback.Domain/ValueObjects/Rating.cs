using Feedback.Domain.Common;
using Feedback.Domain.Exceptions;

namespace Feedback.Domain.ValueObjects;

/// <summary>Оцінка 1..5 — незмінний value object. У BSON зберігається як число (власний серіалізатор в Infrastructure).</summary>
public sealed class Rating : ValueObject
{
    public const int Min = 1;
    public const int Max = 5;

    public int Value { get; }

    public Rating(int value)
    {
        if (value is < Min or > Max) throw new DomainException($"Rating must be between {Min} and {Max}");
        Value = value;
    }

    protected override IEnumerable<object?> GetEqualityComponents()
    {
        yield return Value;
    }

    public override string ToString() => Value.ToString();
}
