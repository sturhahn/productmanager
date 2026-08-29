using Microsoft.EntityFrameworkCore;
using ProductManager.Domain.Common;

namespace ProductManager.Api.Persistence.Configurations;

internal static class ConfigurationExtensions
{
    /// <summary>Maps a Money value object as two columns with a shared prefix.</summary>
    public static void OwnMoney<TOwner>(
        this Microsoft.EntityFrameworkCore.Metadata.Builders.EntityTypeBuilder<TOwner> builder,
        System.Linq.Expressions.Expression<Func<TOwner, Money?>> navigation,
        string columnPrefix)
        where TOwner : class
    {
        builder.OwnsOne(navigation, money =>
        {
            money.Property(m => m.Amount).HasColumnName($"{columnPrefix}Amount").HasPrecision(14, 2);
            money.Property(m => m.Currency).HasColumnName($"{columnPrefix}Currency").HasMaxLength(3);
        });
    }

    /// <summary>Maps a Money value object inside an owned type as two columns with a shared prefix.</summary>
    public static void OwnMoney<TOwner, TDependent>(
        this Microsoft.EntityFrameworkCore.Metadata.Builders.OwnedNavigationBuilder<TOwner, TDependent> builder,
        System.Linq.Expressions.Expression<Func<TDependent, Money?>> navigation,
        string columnPrefix)
        where TOwner : class
        where TDependent : class
    {
        builder.OwnsOne(navigation, money =>
        {
            money.Property(m => m.Amount).HasColumnName($"{columnPrefix}Amount").HasPrecision(14, 2);
            money.Property(m => m.Currency).HasColumnName($"{columnPrefix}Currency").HasMaxLength(3);
        });
    }

    /// <summary>Maps a Period value object inside an owned type as Year/Month columns.</summary>
    public static void OwnPeriod<TOwner, TDependent>(
        this Microsoft.EntityFrameworkCore.Metadata.Builders.OwnedNavigationBuilder<TOwner, TDependent> builder,
        System.Linq.Expressions.Expression<Func<TDependent, Period?>> navigation)
        where TOwner : class
        where TDependent : class
    {
        builder.OwnsOne(navigation, period =>
        {
            period.Property(p => p.Year).HasColumnName("PeriodYear");
            period.Property(p => p.Month).HasColumnName("PeriodMonth");
        });
    }

    public static Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<TEnum> AsString<TEnum>(
        this Microsoft.EntityFrameworkCore.Metadata.Builders.PropertyBuilder<TEnum> property)
    {
        return property.HasConversion<string>().HasMaxLength(40);
    }
}
