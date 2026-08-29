namespace ProductManager.Domain.Common;

/// <summary>An amount in a currency. Immutable value object.</summary>
public sealed record Money
{
    public decimal Amount { get; }
    public string Currency { get; }

    private Money() { Currency = "USD"; } // EF

    public Money(decimal amount, string currency)
    {
        if (string.IsNullOrWhiteSpace(currency) || currency.Trim().Length != 3)
            throw new ArgumentException("Currency must be a 3-letter ISO code.", nameof(currency));
        if (amount < 0)
            throw new ArgumentOutOfRangeException(nameof(amount), "Money amount cannot be negative.");
        Amount = amount;
        Currency = currency.Trim().ToUpperInvariant();
    }

    public static Money Zero(string currency = "USD") => new(0m, currency);

    public Money Add(Money other)
    {
        if (other.Currency != Currency)
            throw new DomainConflictException($"Cannot add {other.Currency} to {Currency}.");
        return new Money(Amount + other.Amount, Currency);
    }

    public override string ToString() => $"{Amount:0.##} {Currency}";
}
