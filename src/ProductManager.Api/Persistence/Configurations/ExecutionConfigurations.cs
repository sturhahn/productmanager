using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductManager.Domain.Execution;
using ProductManager.Domain.Seam;

namespace ProductManager.Api.Persistence.Configurations;

public class WorkspaceConfiguration : IEntityTypeConfiguration<Workspace>
{
    public void Configure(EntityTypeBuilder<Workspace> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasIndex(x => x.Name).IsUnique();
    }
}

public class RoadmapConfiguration : IEntityTypeConfiguration<Roadmap>
{
    public void Configure(EntityTypeBuilder<Roadmap> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.VersionName).HasMaxLength(200);
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Roadmap>().WithMany().HasForeignKey(x => x.ParentRoadmapId).OnDelete(DeleteBehavior.SetNull);
        b.HasIndex(x => new { x.WorkspaceId, x.IsLive });
        b.HasMany(x => x.Items).WithOne().HasForeignKey(i => i.RoadmapId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Items).AutoInclude();
    }
}

public class RoadmapItemConfiguration : IEntityTypeConfiguration<RoadmapItem>
{
    public void Configure(EntityTypeBuilder<RoadmapItem> b)
    {
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.RoadmapId, x.ProjectId }).IsUnique();
    }
}

public class InitiativeConfiguration : IEntityTypeConfiguration<Initiative>
{
    public void Configure(EntityTypeBuilder<Initiative> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.StrategicIntent).AsString();
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();
        // TargetSegmentIds / TargetNeedIds map to uuid[] columns via Npgsql array support.
    }
}

public class ProjectConfiguration : IEntityTypeConfiguration<Project>
{
    public void Configure(EntityTypeBuilder<Project> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.Status).AsString();
        b.HasOne<Initiative>().WithMany().HasForeignKey(x => x.InitiativeId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => x.InitiativeId);

        b.HasMany(x => x.Dependencies).WithOne().HasForeignKey(d => d.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Dependencies).AutoInclude();

        b.HasMany(x => x.DeliveredFeatures).WithOne().HasForeignKey(f => f.ProjectId).OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.DeliveredFeatures).AutoInclude();

        b.OwnsMany(x => x.EffortDemands, demand =>
        {
            demand.ToTable("ProjectEffortDemands");
            demand.WithOwner().HasForeignKey("ProjectId");
            demand.Property<int>("Id");
            demand.HasKey("Id");
            demand.OwnPeriod(d => d.Period);
            demand.Property(d => d.Points).HasPrecision(10, 2);
            demand.HasIndex(d => d.TeamId);
        });
    }
}

public class ProjectDependencyConfiguration : IEntityTypeConfiguration<ProjectDependency>
{
    public void Configure(EntityTypeBuilder<ProjectDependency> b)
    {
        b.ToTable("ProjectDependencies");
        b.HasKey(x => new { x.ProjectId, x.DependsOnProjectId });
        b.HasOne<Project>().WithMany().HasForeignKey(x => x.DependsOnProjectId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class ProjectFeatureConfiguration : IEntityTypeConfiguration<ProjectFeature>
{
    public void Configure(EntityTypeBuilder<ProjectFeature> b)
    {
        b.ToTable("ProjectFeatures");
        b.HasKey(x => new { x.ProjectId, x.FeatureId });
        b.HasOne<Feature>().WithMany().HasForeignKey(x => x.FeatureId).OnDelete(DeleteBehavior.Cascade);
    }
}

public class TeamConfiguration : IEntityTypeConfiguration<Team>
{
    public void Configure(EntityTypeBuilder<Team> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.SpecializationTag).HasMaxLength(100);
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        b.HasIndex(x => new { x.WorkspaceId, x.Name }).IsUnique();

        b.OwnsMany(x => x.Capacities, capacity =>
        {
            capacity.ToTable("TeamCapacities");
            capacity.WithOwner().HasForeignKey("TeamId");
            capacity.Property<int>("Id");
            capacity.HasKey("Id");
            capacity.OwnPeriod(c => c.Period);
            capacity.Property(c => c.Points).HasPrecision(10, 2);
        });
        b.Navigation(x => x.Capacities).AutoInclude();
    }
}
