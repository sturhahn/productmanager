using System.ComponentModel.DataAnnotations;

namespace ProductManager.Api.Auth;

/// <summary>
/// Single-user auth configuration (Phase 1). One shared password issues a JWT; real tenancy,
/// user accounts and SSO are deferred.
/// </summary>
public sealed class AuthOptions
{
    public const string SectionName = "Auth";

    /// <summary>The single-user password checked by <c>POST /api/auth/token</c>.</summary>
    [Required]
    public string Password { get; set; } = string.Empty;

    /// <summary>Symmetric signing key. Must be at least 32 bytes (HMAC-SHA256).</summary>
    [Required]
    [MinLength(32)]
    public string SigningKey { get; set; } = string.Empty;

    public string Issuer { get; set; } = "productmanager";

    public string Audience { get; set; } = "productmanager";

    [Range(1, 60 * 24 * 30)]
    public int TokenLifetimeMinutes { get; set; } = 720;

    /// <summary>Subject claim of the single user.</summary>
    public string Subject { get; set; } = "pm";
}
