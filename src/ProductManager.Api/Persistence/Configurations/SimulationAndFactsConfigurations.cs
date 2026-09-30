using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using ProductManager.Domain.Execution;
using ProductManager.Domain.Facts;
using ProductManager.Domain.Imports;
using ProductManager.Domain.Simulation;

namespace ProductManager.Api.Persistence.Configurations;

public class AssumptionSetConfiguration : IEntityTypeConfiguration<AssumptionSet>
{
    public void Configure(EntityTypeBuilder<AssumptionSet> b)
    {
        b.Property(x => x.Name).HasMaxLength(200);
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AssumptionSet>().WithMany().HasForeignKey(x => x.ParentAssumptionSetId)
            .OnDelete(DeleteBehavior.SetNull);
        b.HasMany(x => x.Parameters).WithOne().HasForeignKey(p => p.AssumptionSetId)
            .OnDelete(DeleteBehavior.Cascade);
        b.Navigation(x => x.Parameters).AutoInclude();
    }
}

public class ParameterConfiguration : IEntityTypeConfiguration<Parameter>
{
    public void Configure(EntityTypeBuilder<Parameter> b)
    {
        b.Property(x => x.Key).HasMaxLength(300);
        b.Property(x => x.Provenance).AsString();
        b.Property(x => x.Rationale).HasMaxLength(4000);
        b.Property(x => x.FlowKind).AsString();
        b.HasIndex(x => new { x.AssumptionSetId, x.Key }).IsUnique();
        b.HasIndex(x => new { x.ClientSegmentId, x.FlowKind });
        b.Ignore(x => x.IsEngineParameter);
        // Deliberately no FK on ClientSegmentId / ModuleProductId / CovariateId: an assumption set
        // is an immutable-ish artifact that runs reference, and deleting a segment must not silently
        // rewrite the assumptions a past run was executed against.
        b.OwnsOne(x => x.Distribution, dist =>
        {
            dist.Property(d => d.Type).HasColumnName("DistributionType").HasConversion<string>().HasMaxLength(40);
            dist.Property(d => d.ParamA).HasColumnName("DistributionParamA");
            dist.Property(d => d.ParamB).HasColumnName("DistributionParamB");
        });
        b.Navigation(x => x.Distribution).IsRequired();
    }
}

public class CalibrationRunConfiguration : IEntityTypeConfiguration<CalibrationRun>
{
    public void Configure(EntityTypeBuilder<CalibrationRun> b)
    {
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<AssumptionSet>().WithMany().HasForeignKey(x => x.PriorAssumptionSetId)
            .OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AssumptionSet>().WithMany().HasForeignKey(x => x.FittedAssumptionSetId)
            .OnDelete(DeleteBehavior.Restrict);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Property(x => x.InputSnapshotJson).HasColumnType("jsonb");
        b.Property(x => x.OutputJson).HasColumnType("jsonb");
        b.Ignore(x => x.IsExecuted);
        b.HasIndex(x => x.CreatedAt);
    }
}

public class SimulationRunConfiguration : IEntityTypeConfiguration<SimulationRun>
{
    public void Configure(EntityTypeBuilder<SimulationRun> b)
    {
        b.Property(x => x.ModelVersion).HasMaxLength(100);
        b.Property(x => x.Name).HasMaxLength(200);
        b.Ignore(x => x.IsExecuted);
        b.Property(x => x.CovariateScenarioJson).HasColumnType("jsonb");
        b.Property(x => x.EventOverlayJson).HasColumnType("jsonb");
        b.Property(x => x.InputSnapshotJson).HasColumnType("jsonb");
        b.Property(x => x.TrajectoriesJson).HasColumnType("jsonb");
        b.Property(x => x.DiagnosticsJson).HasColumnType("jsonb");
        b.Property(x => x.EngineMode).AsString();
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        b.HasOne<Roadmap>().WithMany().HasForeignKey(x => x.RoadmapId).OnDelete(DeleteBehavior.Restrict);
        b.HasOne<AssumptionSet>().WithMany().HasForeignKey(x => x.AssumptionSetId).OnDelete(DeleteBehavior.Restrict);
        b.HasIndex(x => x.CreatedAt);
    }
}

public class FactEventConfiguration : IEntityTypeConfiguration<FactEvent>
{
    public void Configure(EntityTypeBuilder<FactEvent> b)
    {
        b.Property(x => x.Kind).AsString();
        b.Property(x => x.SubjectType).HasMaxLength(100);
        b.Property(x => x.PayloadJson).HasColumnType("jsonb");
        b.Property(x => x.Notes).HasMaxLength(4000);
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        // Bi-temporal query paths: "what happened when" and "what did we know as of".
        b.HasIndex(x => x.EffectiveDate);
        b.HasIndex(x => x.RecordedDate);
        b.HasIndex(x => new { x.Kind, x.EffectiveDate });
        b.HasIndex(x => new { x.SubjectType, x.SubjectId });
    }
}

public class SourceBindingConfiguration : IEntityTypeConfiguration<SourceBinding>
{
    public void Configure(EntityTypeBuilder<SourceBinding> b)
    {
        b.Property(x => x.SourceSystem).HasMaxLength(SourceBinding.MaxKeyPartLength);
        b.Property(x => x.SourceInstance).HasMaxLength(SourceBinding.MaxKeyPartLength);
        b.Property(x => x.ObjectKind).HasMaxLength(SourceBinding.MaxKeyPartLength);
        b.Property(x => x.SourceRecordId).HasMaxLength(SourceBinding.MaxKeyPartLength);
        b.Property(x => x.TargetType).HasMaxLength(100);
        b.Property(x => x.ImportedValuesJson).HasColumnType("jsonb");
        b.HasOne<Workspace>().WithMany().HasForeignKey(x => x.WorkspaceId).OnDelete(DeleteBehavior.Cascade);
        // The identity rule itself: one source record, one target, per workspace. Workspace rather
        // than market scope because opportunities, relationships and facts are bound too, and facts
        // have no market. No FK on TargetId: it points at one of several tables.
        b.HasIndex(x => new { x.WorkspaceId, x.SourceSystem, x.SourceInstance, x.ObjectKind, x.SourceRecordId })
            .IsUnique();
        b.HasIndex(x => x.TargetId);
    }
}
