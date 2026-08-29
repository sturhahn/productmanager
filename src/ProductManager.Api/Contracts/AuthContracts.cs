using System.ComponentModel.DataAnnotations;

namespace ProductManager.Api.Contracts;

/// <summary>Single-user sign-in: the shared password from configuration.</summary>
public sealed record TokenRequest([Required] string Password);

/// <summary>A bearer token to send as <c>Authorization: Bearer &lt;accessToken&gt;</c>.</summary>
public sealed record TokenResponse(string AccessToken, string TokenType, DateTimeOffset ExpiresAt);
