namespace NutriForge.Application.Authorization;

/// <summary>The two v1 roles (SPEC RBAC). Deliberately small — single-user B2C.</summary>
public static class Roles
{
    /// <summary>Owns and manages only their own data; reads the shared catalog.</summary>
    public const string User = "user";

    /// <summary>Operates the food catalog (imports, verification tiers); no access to user data.</summary>
    public const string Admin = "admin";
}

/// <summary>
/// Authorization policy names. Code references these, never a role string directly, so the
/// role→policy mapping is changed in one place.
/// </summary>
public static class Policies
{
    /// <summary>Any authenticated end user acting on their own data.</summary>
    public const string OwnerOnly = nameof(OwnerOnly);

    /// <summary>The data-steward role (imports, verification).</summary>
    public const string AdminOnly = nameof(AdminOnly);
}
