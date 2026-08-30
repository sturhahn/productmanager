using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductManager.Domain.Execution;
using ProductManager.Domain.Market;
using ProductManager.Domain.Seam;

namespace ProductManager.Api.Persistence.Configurations;

public class FeatureConfiguration : IEntityTypeConfiguration<Feature>
{
    public void Configure(EntityTypeBuilder<Feature> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Status).AsString();
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();

        b.OwnsMany(x => x.Deltas, delta =>
        {
            delta.ToTable("FeatureDeltas");
            delta.WithOwner().HasForeignKey("FeatureId");
            delta.Property<int>("Id");
            delta.HasKey("Id");
            delta.Property(d => d.Kind).AsString();
            delta.Property(d => d.FromTier).AsString();
            delta.Property(d => d.ToTier).AsString();
            delta.Property(d => d.Rationale).HasMaxLength(2000);
        });
        b.Navigation(x => x.Deltas).AutoInclude();
    }
}

public class MarketConfiguration : IEntityTypeConfiguration<Market>
{
    public void Configure(EntityTypeBuilder<Market> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
    }
}

public class ClientSegmentConfiguration : IEntityTypeConfiguration<ClientSegment>
{
    public void Configure(EntityTypeBuilder<ClientSegment> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<Market>().WithMany().HasForeignKey(x => x.MarketId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.MarketId, x.Name }).IsUnique();

        // The observed starting state the engine consumes: one measurement, stored inline.
        b.OwnsOne(x => x.StockState, stock =>
        {
            stock.Property(s => s.InitialProspectPool).HasColumnName("InitialProspectPool");
            stock.Property(s => s.InitialClientCount).HasColumnName("InitialClientCount");
            stock.Property(s => s.ArpuPerMonth).HasColumnName("ArpuPerMonth");
            stock.Property(s => s.ImplementationFee).HasColumnName("ImplementationFee");
            stock.Property(s => s.ServicingCostPerClient).HasColumnName("ServicingCostPerClient");
            stock.Property(s => s.InfraFixedCostPerMonth).HasColumnName("InfraFixedCostPerMonth");
            stock.Property(s => s.InfraCostPerClient).HasColumnName("InfraCostPerClient");
            stock.Property(s => s.RdDragPerMonth).HasColumnName("RdDragPerMonth");
        });
        b.Navigation(x => x.StockState).IsRequired();

        b.OwnsMany(x => x.ModulePenetrations, penetration =>
        {
            penetration.ToTable("ClientSegmentModulePenetrations");
            penetration.WithOwner().HasForeignKey("ClientSegmentId");
            penetration.Property<int>("Id");
            penetration.HasKey("Id");
            penetration.HasIndex(p => p.ModuleProductId);
        });
        b.Navigation(x => x.ModulePenetrations).AutoInclude();

        b.OwnsMany(x => x.ApproachAdoptions, adoption =>
        {
            adoption.ToTable("ClientSegmentApproachAdoptions");
            adoption.WithOwner().HasForeignKey("ClientSegmentId");
            adoption.Property<int>("Id");
            adoption.HasKey("Id");
            adoption.HasIndex(a => a.ApproachId);
        });
        b.Navigation(x => x.ApproachAdoptions).AutoInclude();
    }
}

public class FirmConfiguration : IEntityTypeConfiguration<Firm>
{
    public void Configure(EntityTypeBuilder<Firm> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.EpistemicStatus).AsString();
        b.Property(x => x.AttributesJson).HasColumnType("jsonb");
        b.HasOne<Market>().WithMany().HasForeignKey(x => x.MarketId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ClientSegment>().WithMany().HasForeignKey(x => x.ClientSegmentId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.MarketId, x.Name });
        b.HasIndex(x => x.EpistemicStatus);
    }
}

public class CompetitorConfiguration : IEntityTypeConfiguration<Competitor>
{
    public void Configure(EntityTypeBuilder<Competitor> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<Market>().WithMany().HasForeignKey(x => x.MarketId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.MarketId, x.Name }).IsUnique();
    }
}

public class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    public void Configure(EntityTypeBuilder<Product> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<Market>().WithMany().HasForeignKey(x => x.MarketId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Competitor>().WithMany().HasForeignKey(x => x.CompetitorId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ParentProductId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.MarketId, x.Name });

        b.OwnsMany(x => x.Prices, price =>
        {
            price.ToTable("ProductPrices");
            price.WithOwner().HasForeignKey("ProductId");
            price.Property<int>("Id");
            price.HasKey("Id");
            price.OwnMoney(p => p.RecurringPricePerPeriod, "Recurring");
            price.OwnMoney(p => p.ImplementationFee, "ImplementationFee");
            price.Property(p => p.UsageBasedPlaceholder).HasMaxLength(2000);
        });
        b.Navigation(x => x.Prices).AutoInclude();
    }
}

public class FirmProductRelationshipConfiguration : IEntityTypeConfiguration<FirmProductRelationship>
{
    public void Configure(EntityTypeBuilder<FirmProductRelationship> b)
    {
        b.HasOne<Firm>().WithMany().HasForeignKey(x => x.FirmId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.FirmId, x.ProductId });
        b.OwnMoney(x => x.PricePaidPerPeriod, "PricePaid");
        // ServedProcessIds maps to a uuid[] column via Npgsql array support (delimited text
        // elsewhere — see AppDbContext.OnModelCreating).
    }
}

public class IntegrationConfiguration : IEntityTypeConfiguration<Integration>
{
    public void Configure(EntityTypeBuilder<Integration> b)
    {
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductAId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductBId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.ProductAId, x.ProductBId }).IsUnique();
    }
}

public class ChannelConfiguration : IEntityTypeConfiguration<Channel>
{
    public void Configure(EntityTypeBuilder<Channel> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<Market>().WithMany().HasForeignKey(x => x.MarketId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.MarketId, x.Name }).IsUnique();

        b.OwnsMany(x => x.SegmentReaches, reach =>
        {
            reach.ToTable("ChannelSegmentReaches");
            reach.WithOwner().HasForeignKey("ChannelId");
            reach.Property<int>("Id");
            reach.HasKey("Id");
        });
        b.OwnsMany(x => x.ProductAffinities, affinity =>
        {
            affinity.ToTable("ChannelProductAffinities");
            affinity.WithOwner().HasForeignKey("ChannelId");
            affinity.Property<int>("Id");
            affinity.HasKey("Id");
        });
        b.Navigation(x => x.SegmentReaches).AutoInclude();
        b.Navigation(x => x.ProductAffinities).AutoInclude();
    }
}
