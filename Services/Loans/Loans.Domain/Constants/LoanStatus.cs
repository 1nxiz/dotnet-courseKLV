namespace Loans.Domain.Constants;

/// <summary>
/// Статуси видачі (за зразком OrderStatus з eShop). Значення збігаються з CHECK-обмеженням
/// CK_Loans_Status у LoansDb, а допустимі переходи перевіряє процедура usp_ChangeLoanStatus.
/// </summary>
public static class LoanStatus
{
    public const string Submitted = "Submitted";
    public const string AwaitingValidation = "AwaitingValidation";
    public const string Reserved = "Reserved";
    public const string Paid = "Paid";
    public const string Issued = "Issued";
    public const string Returned = "Returned";
    public const string Cancelled = "Cancelled";

    public static readonly IReadOnlySet<string> All = new HashSet<string>
    {
        Submitted, AwaitingValidation, Reserved, Paid, Issued, Returned, Cancelled
    };

    /// <summary>Статуси, у яких примірники вже зарезервовані й мають повернутися на полицю при скасуванні.</summary>
    public static bool HoldsCopies(string status) => status is Reserved or Paid or Issued;
}

public static class DeliveryType
{
    public const string Pickup = "Pickup";
    public const string Courier = "Courier";
}

public static class PaymentMethod
{
    public const string Card = "Card";
    public const string Cash = "Cash";
    public const string Online = "Online";
}
