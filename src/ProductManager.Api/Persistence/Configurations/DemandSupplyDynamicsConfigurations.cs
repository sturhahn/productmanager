using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductManager.Domain.Execution;
using ProductManager.Domain.Market;

namespace ProductManager.Api.Persistence.Configurations;

public class BusinessProcessConfiguration : IEntityTypeConfiguration<BusinessProcess>
{
    public void Configure(EntityTypeBuilder<BusinessProcess> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<Market>().WithMany().HasForeignKey(x => x.MarketId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BusinessProcess>().WithMany().HasForeignKey(x => x.ParentProcessId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => new { x.MarketId, x.Name });
    }
}

public class ApproachConfiguration : IEntityTypeConfiguration<Approach>
{
    public void Configure(EntityTypeBuilder<Approach> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<BusinessProcess>().WithMany().HasForeignKey(x => x.BusinessProcessId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.BusinessProcessId, x.Name }).IsUnique();
    }
}

public class NeedConfiguration : IEntityTypeConfiguration<Need>
{
    public void Configure(EntityTypeBuilder<Need> b)
    {
        b.Property(x => x.RequiredDepthJson).HasColumnType("jsonb");
        b.Property(x => x.Conditionality).HasMaxLength(2000);
        b.HasOne<Firm>().WithMany().HasForeignKey(x => x.FirmId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<ClientSegment>().WithMany().HasForeignKey(x => x.ClientSegmentId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BusinessProcess>().WithMany().HasForeignKey(x => x.BusinessProcessId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.FirmId);
        b.HasIndex(x => x.ClientSegmentId);
        b.HasIndex(x => x.BusinessProcessId);
        b.OwnMoney(x => x.WorkaroundCostPerPeriod, "WorkaroundCost");
        b.ToTable(t => t.HasCheckConstraint(
            "CK_Needs_ExactlyOneSubject",
            "(\"FirmId\" IS NULL) <> (\"ClientSegmentId\" IS NULL)"));
    }
}

public class CoverageConfiguration : IEntityTypeConfiguration<Coverage>
{
    public void Configure(EntityTypeBuilder<Coverage> b)
    {
        b.Property(x => x.Tier).AsString();
        b.Property(x => x.ScaleLimits).HasMaxLength(2000);
        b.Property(x => x.Rationale).HasMaxLength(2000);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.ProductId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<BusinessProcess>().WithMany().HasForeignKey(x => x.BusinessProcessId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Approach>().WithMany().HasForeignKey(x => x.ApproachId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<ClientSegment>().WithMany().HasForeignKey(x => x.ClientSegmentId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.ProductId, x.BusinessProcessId });
        b.Ignore(x => x.NumericScore);
    }
}

public class OpportunityConfiguration : IEntityTypeConfiguration<Opportunity>
{
    public void Configure(EntityTypeBuilder<Opportunity> b)
    {
        b.Property(x => x.Stage).AsString();
        b.HasOne<Firm>().WithMany().HasForeignKey(x => x.FirmId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Channel>().WithMany().HasForeignKey(x => x.ChannelId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.WinningProductId).OnDelete(DeleteBehavior.SetNull);
        b.HasOne<Product>().WithMany().HasForeignKey(x => x.CrossSellModuleProductId)
            .OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => x.FirmId);
        b.HasIndex(x => x.Stage);
        b.Ignore(x => x.IsCrossSell);
        b.Ignore(x => x.IsDecided);
        // CompetitorIds maps to a uuid[] column via Npgsql array support.

        b.OwnsMany(x => x.Scope, scope =>
        {
            scope.ToTable("OpportunityScopeItems");
            scope.WithOwner().HasForeignKey("OpportunityId");
            scope.Property<int>("Id");
            scope.HasKey("Id");
            scope.HasIndex(s => s.BusinessProcessId);
        });
        b.Navigation(x => x.Scope).AutoInclude();
    }
}

public class MarketEventConfiguration : IEntityTypeConfiguration<MarketEvent>
{
    public void Configure(EntityTypeBuilder<MarketEvent> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Type).AsString();
        b.HasOne<Market>().WithMany().HasForeignKey(x => x.MarketId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Competitor>().WithMany().HasForeignKey(x => x.CompetitorId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.MarketId, x.StartDate });
        b.HasIndex(x => x.IsHypothetical);

        b.OwnsMany(x => x.RateMultipliers, multiplier =>
        {
            multiplier.ToTable("MarketEventRateMultipliers");
            multiplier.WithOwner().HasForeignKey("MarketEventId");
            multiplier.Property<int>("Id");
            multiplier.HasKey("Id");
            multiplier.Property(m => m.FlowKind).AsString();
            multiplier.HasIndex(m => m.ClientSegmentId);
        });
        b.Navigation(x => x.RateMultipliers).AutoInclude();
    }
}

public class CovariateConfiguration : IEntityTypeConfiguration<Covariate>
{
    public void Configure(EntityTypeBuilder<Covariate> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Unit).HasMaxLength(50);
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
        b.Ignore(x => x.HasGbmSpec);
    }
}
