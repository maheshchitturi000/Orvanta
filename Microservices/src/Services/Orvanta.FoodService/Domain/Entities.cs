namespace Orvanta.FoodService.Domain;

/// <summary>A user's OAuth connection to an external provider. Tokens are AES-GCM encrypted at rest.</summary>
public sealed class ProviderConnection
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;

    public string AccessTokenEncrypted { get; set; } = string.Empty;
    public string? RefreshTokenEncrypted { get; set; }
    public DateTime AccessTokenExpiresAtUtc { get; set; }
    public string? Scope { get; set; }

    public DateTime ConnectedAtUtc { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>In-flight OAuth authorization (state + PKCE verifier). Single use, short lived.</summary>
public sealed class PendingAuthorization
{
    /// <summary>SHA-256 of the state parameter.</summary>
    public string StateHash { get; set; } = string.Empty;
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string CodeVerifierEncrypted { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public DateTime ExpiresAtUtc { get; set; }
}

/// <summary>Client id obtained through Dynamic Client Registration, cached per provider + redirect URI.</summary>
public sealed class OAuthClientRegistration
{
    public string Provider { get; set; } = string.Empty;
    public string RedirectUri { get; set; } = string.Empty;
    public string ClientId { get; set; } = string.Empty;
    public string? ClientSecretEncrypted { get; set; }
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Delivery address cached from a provider so the UI doesn't hit the provider every time.</summary>
public sealed class SavedAddress
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string ExternalId { get; set; } = string.Empty;
    public string? Label { get; set; }
    public string? AddressLine { get; set; }
    public string? PhoneNumber { get; set; }

    /// <summary>The address used for search and ordering with this provider.</summary>
    public bool IsDefault { get; set; }
    public DateTime SyncedAtUtc { get; set; } = DateTime.UtcNow;
}

/// <summary>Order placed by Orvanta on the user's behalf.</summary>
public sealed class FoodOrder
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public Guid UserId { get; set; }
    public string Provider { get; set; } = string.Empty;
    public string? ProviderOrderId { get; set; }
    public string RestaurantId { get; set; } = string.Empty;
    public string RestaurantName { get; set; } = string.Empty;
    public string MenuItemId { get; set; } = string.Empty;
    public string ItemName { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public decimal TotalAmount { get; set; }
    public string PaymentMethod { get; set; } = "COD";
    public string AddressId { get; set; } = string.Empty;
    public string Status { get; set; } = "Placed";
    public DateTime CreatedAtUtc { get; set; } = DateTime.UtcNow;
}
