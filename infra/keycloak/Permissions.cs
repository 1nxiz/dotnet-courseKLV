namespace LibraryKeycloak;

/// <summary>
/// Resource Servers і їхні scopes (Таблиця 6) та матриця ролей (Таблиця 7) — та сама, що й Permissions.RoleMatrix
/// у Platform.ServiceDefaults, де мікросервіси перевіряють permissions локально.
/// </summary>
public static class Matrix
{
    public static readonly Dictionary<string, string[]> ResourceServers = new()
    {
        ["loans-api"] = ["loans:read", "loans:create", "loans:update", "loans:delete", "loans:approve"],
        ["books-api"] = ["books:read", "books:create", "books:update", "books:delete", "books:publish"],
        ["payments-api"] = ["payments:read", "payments:process", "payments:refund"],
        ["feedback-api"] = ["feedback:read", "feedback:write"]
    };

    public static readonly string[] Roles = ["admin", "manager", "user", "guest"];

    /// <summary>payments-api свідомо не видає permissions нікому, крім admin.</summary>
    public static readonly Dictionary<string, string[]> RolePermissions = new()
    {
        ["admin"] = ResourceServers.Values.SelectMany(s => s).ToArray(),
        ["manager"] = ["loans:read", "loans:update", "loans:approve", "books:read", "books:update", "books:publish", "feedback:read", "feedback:write"],
        ["user"] = ["loans:read", "loans:create", "books:read", "feedback:read", "feedback:write"],
        ["guest"] = ["books:read", "feedback:read"]
    };

    /// <summary>Тестові користувачі — по одному на роль.</summary>
    public static readonly (string UserName, string FirstName, string Role)[] Users =
    [
        ("alice", "Alice", "admin"), ("mike", "Mike", "manager"), ("uma", "Uma", "user"), ("gary", "Gary", "guest")
    ];

    /// <summary>Client Credentials для Aggregator: лише читання трьох сервісів.</summary>
    public static readonly string[] AggregatorPermissions = ["loans:read", "books:read", "feedback:read"];
}
