namespace Microsoft.Extensions.Hosting;

/// <summary>
/// Каталог permissions платформи (Лаб. 09) і матриця «роль → permissions» — технічне завдання для [RequirePermission].
/// Ролі визначені централізовано (Keycloak realm roles, ролі IdentityServer / Auth Service), а РІШЕННЯ приймає кожен
/// мікросервіс локально на рівні endpoint. Та сама матриця описана в Pulumi (infra/keycloak) як Permission Policies.
/// </summary>
public static class Permissions
{
    public static class Loans                       // loans-api (аналог orders-api)
    {
        public const string Read = "loans:read";
        public const string Create = "loans:create";
        public const string Update = "loans:update";
        public const string Delete = "loans:delete";
        public const string Approve = "loans:approve";
        public static readonly string[] All = [Read, Create, Update, Delete, Approve];
    }

    public static class Books                       // books-api (аналог products-api)
    {
        public const string Read = "books:read";
        public const string Create = "books:create";
        public const string Update = "books:update";
        public const string Delete = "books:delete";
        public const string Publish = "books:publish";
        public static readonly string[] All = [Read, Create, Update, Delete, Publish];
    }

    public static class Payments                    // payments-api — лише admin
    {
        public const string Read = "payments:read";
        public const string Process = "payments:process";
        public const string Refund = "payments:refund";
        public static readonly string[] All = [Read, Process, Refund];
    }

    public static class Feedback                    // feedback-api (відгуки)
    {
        public const string Read = "feedback:read";
        public const string Write = "feedback:write";
        public static readonly string[] All = [Read, Write];
    }

    public static readonly string[] All = [.. Loans.All, .. Books.All, .. Payments.All, .. Feedback.All];

    /// <summary>Ролі платформи (нижній регістр; Admin/User з Auth Service зіставляються без урахування регістру).</summary>
    public static class Roles
    {
        public const string Admin = "admin";
        public const string Manager = "manager";
        public const string User = "user";
        public const string Guest = "guest";
    }

    /// <summary>
    /// Матриця ролей (Таблиця 7 методички): admin — усе; manager — частково; user — базово; guest — лише читання каталогу
    /// (і відгуків). payments-api не видає permissions НІКОМУ, крім admin, — свідоме рішення: найчутливіший ресурс.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string[]> RoleMatrix = new Dictionary<string, string[]>(StringComparer.OrdinalIgnoreCase)
    {
        [Roles.Admin] = All,
        [Roles.Manager] = [Loans.Read, Loans.Update, Loans.Approve, Books.Read, Books.Update, Books.Publish, Feedback.Read, Feedback.Write],
        [Roles.User] = [Loans.Read, Loans.Create, Books.Read, Feedback.Read, Feedback.Write],
        [Roles.Guest] = [Books.Read, Feedback.Read]
    };
}
