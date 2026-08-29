using System;
using System.Collections.Generic;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace ProductManager.Api.Persistence.Migrations
{
    /// <inheritdoc />
    public partial class InitialSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Workspaces",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Workspaces", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "AssumptionSets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    ParentAssumptionSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AssumptionSets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_AssumptionSets_AssumptionSets_ParentAssumptionSetId",
                        column: x => x.ParentAssumptionSetId,
                        principalTable: "AssumptionSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_AssumptionSets_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Covariates",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Unit = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Covariates", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Covariates_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FactEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: false),
                    RecordedDate = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    SubjectType = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    SubjectId = table.Column<Guid>(type: "uuid", nullable: true),
                    PayloadJson = table.Column<string>(type: "jsonb", nullable: true),
                    Notes = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FactEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FactEvents_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Initiatives",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Outcome = table.Column<string>(type: "text", nullable: true),
                    HypothesizedValue = table.Column<string>(type: "text", nullable: true),
                    TargetSegmentIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    TargetNeedIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Initiatives", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Initiatives_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Markets",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Markets", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Markets_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Roadmaps",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    VersionName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: true),
                    ParentRoadmapId = table.Column<Guid>(type: "uuid", nullable: true),
                    IsLive = table.Column<bool>(type: "boolean", nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Roadmaps", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Roadmaps_Roadmaps_ParentRoadmapId",
                        column: x => x.ParentRoadmapId,
                        principalTable: "Roadmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Roadmaps_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Teams",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    SpecializationTag = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Teams", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Teams_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "CalibrationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    FactsAsOfDate = table.Column<DateOnly>(type: "date", nullable: false),
                    PriorAssumptionSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    FittedAssumptionSetId = table.Column<Guid>(type: "uuid", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CalibrationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_CalibrationRuns_AssumptionSets_FittedAssumptionSetId",
                        column: x => x.FittedAssumptionSetId,
                        principalTable: "AssumptionSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalibrationRuns_AssumptionSets_PriorAssumptionSetId",
                        column: x => x.PriorAssumptionSetId,
                        principalTable: "AssumptionSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_CalibrationRuns_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Parameters",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    AssumptionSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    Key = table.Column<string>(type: "character varying(300)", maxLength: 300, nullable: false),
                    DistributionType = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    DistributionParamA = table.Column<double>(type: "double precision", nullable: false),
                    DistributionParamB = table.Column<double>(type: "double precision", nullable: false),
                    Provenance = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Rationale = table.Column<string>(type: "character varying(4000)", maxLength: 4000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Parameters", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Parameters_AssumptionSets_AssumptionSetId",
                        column: x => x.AssumptionSetId,
                        principalTable: "AssumptionSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    InitiativeId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Projects_Initiatives_InitiativeId",
                        column: x => x.InitiativeId,
                        principalTable: "Initiatives",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "BusinessProcesses",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<Guid>(type: "uuid", nullable: false),
                    ParentProcessId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BusinessProcesses", x => x.Id);
                    table.ForeignKey(
                        name: "FK_BusinessProcesses_BusinessProcesses_ParentProcessId",
                        column: x => x.ParentProcessId,
                        principalTable: "BusinessProcesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_BusinessProcesses_Markets_MarketId",
                        column: x => x.MarketId,
                        principalTable: "Markets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Channels",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    OriginatesOpportunities = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Channels", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Channels_Markets_MarketId",
                        column: x => x.MarketId,
                        principalTable: "Markets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ClientSegments",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    SuiteAffinity = table.Column<double>(type: "double precision", nullable: false),
                    ConsolidationPreference = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClientSegments", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ClientSegments_Markets_MarketId",
                        column: x => x.MarketId,
                        principalTable: "Markets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Competitors",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StrategyNotes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Competitors", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Competitors_Markets_MarketId",
                        column: x => x.MarketId,
                        principalTable: "Markets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "SimulationRuns",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadmapId = table.Column<Guid>(type: "uuid", nullable: false),
                    AssumptionSetId = table.Column<Guid>(type: "uuid", nullable: false),
                    CovariateScenarioJson = table.Column<string>(type: "jsonb", nullable: true),
                    EventOverlayJson = table.Column<string>(type: "jsonb", nullable: true),
                    Seed = table.Column<int>(type: "integer", nullable: false),
                    ModelVersion = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: true),
                    InputSnapshotJson = table.Column<string>(type: "jsonb", nullable: true),
                    TrajectoriesJson = table.Column<string>(type: "jsonb", nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SimulationRuns", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SimulationRuns_AssumptionSets_AssumptionSetId",
                        column: x => x.AssumptionSetId,
                        principalTable: "AssumptionSets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SimulationRuns_Roadmaps_RoadmapId",
                        column: x => x.RoadmapId,
                        principalTable: "Roadmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_SimulationRuns_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "TeamCapacities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    PeriodYear = table.Column<int>(type: "integer", nullable: false),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_TeamCapacities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_TeamCapacities_Teams_TeamId",
                        column: x => x.TeamId,
                        principalTable: "Teams",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectDependencies",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    DependsOnProjectId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectDependencies", x => new { x.ProjectId, x.DependsOnProjectId });
                    table.ForeignKey(
                        name: "FK_ProjectDependencies_Projects_DependsOnProjectId",
                        column: x => x.DependsOnProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectDependencies_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectEffortDemands",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    TeamId = table.Column<Guid>(type: "uuid", nullable: false),
                    PeriodYear = table.Column<int>(type: "integer", nullable: false),
                    PeriodMonth = table.Column<int>(type: "integer", nullable: false),
                    Points = table.Column<decimal>(type: "numeric(10,2)", precision: 10, scale: 2, nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectEffortDemands", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProjectEffortDemands_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RoadmapItems",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    RoadmapId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    PlannedStart = table.Column<DateOnly>(type: "date", nullable: false),
                    PlannedEnd = table.Column<DateOnly>(type: "date", nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RoadmapItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_RoadmapItems_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_RoadmapItems_Roadmaps_RoadmapId",
                        column: x => x.RoadmapId,
                        principalTable: "Roadmaps",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Approaches",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Approaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Approaches_BusinessProcesses_BusinessProcessId",
                        column: x => x.BusinessProcessId,
                        principalTable: "BusinessProcesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelProductAffinities",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    Bias = table.Column<double>(type: "double precision", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelProductAffinities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelProductAffinities_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ChannelSegmentReaches",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientSegmentId = table.Column<Guid>(type: "uuid", nullable: false),
                    Reach = table.Column<double>(type: "double precision", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChannelSegmentReaches", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChannelSegmentReaches_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Firms",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<Guid>(type: "uuid", nullable: false),
                    ClientSegmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    EpistemicStatus = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    AttributesJson = table.Column<string>(type: "jsonb", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Firms", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Firms_ClientSegments_ClientSegmentId",
                        column: x => x.ClientSegmentId,
                        principalTable: "ClientSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Firms_Markets_MarketId",
                        column: x => x.MarketId,
                        principalTable: "Markets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "MarketEvents",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<Guid>(type: "uuid", nullable: false),
                    Type = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    StartDate = table.Column<DateOnly>(type: "date", nullable: false),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Magnitude = table.Column<double>(type: "double precision", nullable: true),
                    IsHypothetical = table.Column<bool>(type: "boolean", nullable: false),
                    CompetitorId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_MarketEvents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_MarketEvents_Competitors_CompetitorId",
                        column: x => x.CompetitorId,
                        principalTable: "Competitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_MarketEvents_Markets_MarketId",
                        column: x => x.MarketId,
                        principalTable: "Markets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Products",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    MarketId = table.Column<Guid>(type: "uuid", nullable: false),
                    CompetitorId = table.Column<Guid>(type: "uuid", nullable: true),
                    ParentProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Products", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Products_Competitors_CompetitorId",
                        column: x => x.CompetitorId,
                        principalTable: "Competitors",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Products_Markets_MarketId",
                        column: x => x.MarketId,
                        principalTable: "Markets",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Products_Products_ParentProductId",
                        column: x => x.ParentProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Needs",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientSegmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    BusinessProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ImportanceWeight = table.Column<double>(type: "double precision", nullable: false),
                    RequiredDepthJson = table.Column<string>(type: "jsonb", nullable: true),
                    Conditionality = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    IsHard = table.Column<bool>(type: "boolean", nullable: false),
                    WorkaroundCostAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    WorkaroundCostCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Needs", x => x.Id);
                    table.CheckConstraint("CK_Needs_ExactlyOneSubject", "(\"FirmId\" IS NULL) <> (\"ClientSegmentId\" IS NULL)");
                    table.ForeignKey(
                        name: "FK_Needs_BusinessProcesses_BusinessProcessId",
                        column: x => x.BusinessProcessId,
                        principalTable: "BusinessProcesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Needs_ClientSegments_ClientSegmentId",
                        column: x => x.ClientSegmentId,
                        principalTable: "ClientSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Needs_Firms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "Firms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Coverages",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    BusinessProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    ApproachId = table.Column<Guid>(type: "uuid", nullable: true),
                    ClientSegmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Tier = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    IsUnknown = table.Column<bool>(type: "boolean", nullable: false),
                    Confidence = table.Column<double>(type: "double precision", nullable: false),
                    ScaleLimits = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    EffectiveDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Coverages", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Coverages_Approaches_ApproachId",
                        column: x => x.ApproachId,
                        principalTable: "Approaches",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Coverages_BusinessProcesses_BusinessProcessId",
                        column: x => x.BusinessProcessId,
                        principalTable: "BusinessProcesses",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Coverages_ClientSegments_ClientSegmentId",
                        column: x => x.ClientSegmentId,
                        principalTable: "ClientSegments",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Coverages_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Features",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    WorkspaceId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: true),
                    Status = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    ShippedDate = table.Column<DateOnly>(type: "date", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Features", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Features_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Features_Workspaces_WorkspaceId",
                        column: x => x.WorkspaceId,
                        principalTable: "Workspaces",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FirmProductRelationships",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false),
                    PricePaidAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: true),
                    PricePaidCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: true),
                    AcquisitionDate = table.Column<DateOnly>(type: "date", nullable: true),
                    EndDate = table.Column<DateOnly>(type: "date", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FirmProductRelationships", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FirmProductRelationships_Firms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "Firms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_FirmProductRelationships_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Integrations",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductAId = table.Column<Guid>(type: "uuid", nullable: false),
                    ProductBId = table.Column<Guid>(type: "uuid", nullable: false),
                    QualityScore = table.Column<double>(type: "double precision", nullable: false),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Integrations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Integrations_Products_ProductAId",
                        column: x => x.ProductAId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Integrations_Products_ProductBId",
                        column: x => x.ProductBId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "Opportunities",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    FirmId = table.Column<Guid>(type: "uuid", nullable: false),
                    ChannelId = table.Column<Guid>(type: "uuid", nullable: true),
                    Stage = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    OpenedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    QualifiedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    DecidedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    CompetitorIds = table.Column<List<Guid>>(type: "uuid[]", nullable: false),
                    WinningProductId = table.Column<Guid>(type: "uuid", nullable: true),
                    Notes = table.Column<string>(type: "text", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Opportunities", x => x.Id);
                    table.ForeignKey(
                        name: "FK_Opportunities_Channels_ChannelId",
                        column: x => x.ChannelId,
                        principalTable: "Channels",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_Opportunities_Firms_FirmId",
                        column: x => x.FirmId,
                        principalTable: "Firms",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_Opportunities_Products_WinningProductId",
                        column: x => x.WinningProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                });

            migrationBuilder.CreateTable(
                name: "ProductPrices",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    ClientSegmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    RecurringAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    RecurringCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    ImplementationFeeAmount = table.Column<decimal>(type: "numeric(14,2)", precision: 14, scale: 2, nullable: false),
                    ImplementationFeeCurrency = table.Column<string>(type: "character varying(3)", maxLength: 3, nullable: false),
                    UsageBasedPlaceholder = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    ProductId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProductPrices", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProductPrices_Products_ProductId",
                        column: x => x.ProductId,
                        principalTable: "Products",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "FeatureDeltas",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    Kind = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: false),
                    BusinessProcessId = table.Column<Guid>(type: "uuid", nullable: true),
                    FromTier = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ToTier = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    ApproachId = table.Column<Guid>(type: "uuid", nullable: true),
                    SegmentId = table.Column<Guid>(type: "uuid", nullable: true),
                    Magnitude = table.Column<double>(type: "double precision", nullable: true),
                    Rationale = table.Column<string>(type: "character varying(2000)", maxLength: 2000, nullable: true),
                    FeatureId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_FeatureDeltas", x => x.Id);
                    table.ForeignKey(
                        name: "FK_FeatureDeltas_Features_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "Features",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectFeatures",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectFeatures", x => new { x.ProjectId, x.FeatureId });
                    table.ForeignKey(
                        name: "FK_ProjectFeatures_Features_FeatureId",
                        column: x => x.FeatureId,
                        principalTable: "Features",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ProjectFeatures_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "OpportunityScopeItems",
                columns: table => new
                {
                    Id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    BusinessProcessId = table.Column<Guid>(type: "uuid", nullable: false),
                    AddedAt = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    OpportunityId = table.Column<Guid>(type: "uuid", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunityScopeItems", x => x.Id);
                    table.ForeignKey(
                        name: "FK_OpportunityScopeItems_Opportunities_OpportunityId",
                        column: x => x.OpportunityId,
                        principalTable: "Opportunities",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_Approaches_BusinessProcessId_Name",
                table: "Approaches",
                columns: new[] { "BusinessProcessId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_AssumptionSets_ParentAssumptionSetId",
                table: "AssumptionSets",
                column: "ParentAssumptionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_AssumptionSets_WorkspaceId",
                table: "AssumptionSets",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcesses_MarketId_Name",
                table: "BusinessProcesses",
                columns: new[] { "MarketId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_BusinessProcesses_ParentProcessId",
                table: "BusinessProcesses",
                column: "ParentProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_CalibrationRuns_FittedAssumptionSetId",
                table: "CalibrationRuns",
                column: "FittedAssumptionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_CalibrationRuns_PriorAssumptionSetId",
                table: "CalibrationRuns",
                column: "PriorAssumptionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_CalibrationRuns_WorkspaceId",
                table: "CalibrationRuns",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_ChannelProductAffinities_ChannelId",
                table: "ChannelProductAffinities",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Channels_MarketId_Name",
                table: "Channels",
                columns: new[] { "MarketId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ChannelSegmentReaches_ChannelId",
                table: "ChannelSegmentReaches",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_ClientSegments_MarketId_Name",
                table: "ClientSegments",
                columns: new[] { "MarketId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Competitors_MarketId_Name",
                table: "Competitors",
                columns: new[] { "MarketId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Covariates_WorkspaceId_Name",
                table: "Covariates",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Coverages_ApproachId",
                table: "Coverages",
                column: "ApproachId");

            migrationBuilder.CreateIndex(
                name: "IX_Coverages_BusinessProcessId",
                table: "Coverages",
                column: "BusinessProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_Coverages_ClientSegmentId",
                table: "Coverages",
                column: "ClientSegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Coverages_ProductId_BusinessProcessId",
                table: "Coverages",
                columns: new[] { "ProductId", "BusinessProcessId" });

            migrationBuilder.CreateIndex(
                name: "IX_FactEvents_EffectiveDate",
                table: "FactEvents",
                column: "EffectiveDate");

            migrationBuilder.CreateIndex(
                name: "IX_FactEvents_Kind_EffectiveDate",
                table: "FactEvents",
                columns: new[] { "Kind", "EffectiveDate" });

            migrationBuilder.CreateIndex(
                name: "IX_FactEvents_RecordedDate",
                table: "FactEvents",
                column: "RecordedDate");

            migrationBuilder.CreateIndex(
                name: "IX_FactEvents_SubjectType_SubjectId",
                table: "FactEvents",
                columns: new[] { "SubjectType", "SubjectId" });

            migrationBuilder.CreateIndex(
                name: "IX_FactEvents_WorkspaceId",
                table: "FactEvents",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_FeatureDeltas_FeatureId",
                table: "FeatureDeltas",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_Features_ProductId",
                table: "Features",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Features_WorkspaceId_Name",
                table: "Features",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_FirmProductRelationships_FirmId_ProductId",
                table: "FirmProductRelationships",
                columns: new[] { "FirmId", "ProductId" });

            migrationBuilder.CreateIndex(
                name: "IX_FirmProductRelationships_ProductId",
                table: "FirmProductRelationships",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Firms_ClientSegmentId",
                table: "Firms",
                column: "ClientSegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Firms_EpistemicStatus",
                table: "Firms",
                column: "EpistemicStatus");

            migrationBuilder.CreateIndex(
                name: "IX_Firms_MarketId_Name",
                table: "Firms",
                columns: new[] { "MarketId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Initiatives_WorkspaceId_Name",
                table: "Initiatives",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_ProductAId_ProductBId",
                table: "Integrations",
                columns: new[] { "ProductAId", "ProductBId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Integrations_ProductBId",
                table: "Integrations",
                column: "ProductBId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketEvents_CompetitorId",
                table: "MarketEvents",
                column: "CompetitorId");

            migrationBuilder.CreateIndex(
                name: "IX_MarketEvents_MarketId_StartDate",
                table: "MarketEvents",
                columns: new[] { "MarketId", "StartDate" });

            migrationBuilder.CreateIndex(
                name: "IX_Markets_WorkspaceId_Name",
                table: "Markets",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Needs_BusinessProcessId",
                table: "Needs",
                column: "BusinessProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_Needs_ClientSegmentId",
                table: "Needs",
                column: "ClientSegmentId");

            migrationBuilder.CreateIndex(
                name: "IX_Needs_FirmId",
                table: "Needs",
                column: "FirmId");

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_ChannelId",
                table: "Opportunities",
                column: "ChannelId");

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_FirmId",
                table: "Opportunities",
                column: "FirmId");

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_Stage",
                table: "Opportunities",
                column: "Stage");

            migrationBuilder.CreateIndex(
                name: "IX_Opportunities_WinningProductId",
                table: "Opportunities",
                column: "WinningProductId");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityScopeItems_BusinessProcessId",
                table: "OpportunityScopeItems",
                column: "BusinessProcessId");

            migrationBuilder.CreateIndex(
                name: "IX_OpportunityScopeItems_OpportunityId",
                table: "OpportunityScopeItems",
                column: "OpportunityId");

            migrationBuilder.CreateIndex(
                name: "IX_Parameters_AssumptionSetId_Key",
                table: "Parameters",
                columns: new[] { "AssumptionSetId", "Key" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_ProductPrices_ProductId",
                table: "ProductPrices",
                column: "ProductId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_CompetitorId",
                table: "Products",
                column: "CompetitorId");

            migrationBuilder.CreateIndex(
                name: "IX_Products_MarketId_Name",
                table: "Products",
                columns: new[] { "MarketId", "Name" });

            migrationBuilder.CreateIndex(
                name: "IX_Products_ParentProductId",
                table: "Products",
                column: "ParentProductId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectDependencies_DependsOnProjectId",
                table: "ProjectDependencies",
                column: "DependsOnProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEffortDemands_ProjectId",
                table: "ProjectEffortDemands",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectEffortDemands_TeamId",
                table: "ProjectEffortDemands",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectFeatures_FeatureId",
                table: "ProjectFeatures",
                column: "FeatureId");

            migrationBuilder.CreateIndex(
                name: "IX_Projects_InitiativeId",
                table: "Projects",
                column: "InitiativeId");

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapItems_ProjectId",
                table: "RoadmapItems",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_RoadmapItems_RoadmapId_ProjectId",
                table: "RoadmapItems",
                columns: new[] { "RoadmapId", "ProjectId" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Roadmaps_ParentRoadmapId",
                table: "Roadmaps",
                column: "ParentRoadmapId");

            migrationBuilder.CreateIndex(
                name: "IX_Roadmaps_WorkspaceId_IsLive",
                table: "Roadmaps",
                columns: new[] { "WorkspaceId", "IsLive" });

            migrationBuilder.CreateIndex(
                name: "IX_SimulationRuns_AssumptionSetId",
                table: "SimulationRuns",
                column: "AssumptionSetId");

            migrationBuilder.CreateIndex(
                name: "IX_SimulationRuns_CreatedAt",
                table: "SimulationRuns",
                column: "CreatedAt");

            migrationBuilder.CreateIndex(
                name: "IX_SimulationRuns_RoadmapId",
                table: "SimulationRuns",
                column: "RoadmapId");

            migrationBuilder.CreateIndex(
                name: "IX_SimulationRuns_WorkspaceId",
                table: "SimulationRuns",
                column: "WorkspaceId");

            migrationBuilder.CreateIndex(
                name: "IX_TeamCapacities_TeamId",
                table: "TeamCapacities",
                column: "TeamId");

            migrationBuilder.CreateIndex(
                name: "IX_Teams_WorkspaceId_Name",
                table: "Teams",
                columns: new[] { "WorkspaceId", "Name" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_Workspaces_Name",
                table: "Workspaces",
                column: "Name",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "CalibrationRuns");

            migrationBuilder.DropTable(
                name: "ChannelProductAffinities");

            migrationBuilder.DropTable(
                name: "ChannelSegmentReaches");

            migrationBuilder.DropTable(
                name: "Covariates");

            migrationBuilder.DropTable(
                name: "Coverages");

            migrationBuilder.DropTable(
                name: "FactEvents");

            migrationBuilder.DropTable(
                name: "FeatureDeltas");

            migrationBuilder.DropTable(
                name: "FirmProductRelationships");

            migrationBuilder.DropTable(
                name: "Integrations");

            migrationBuilder.DropTable(
                name: "MarketEvents");

            migrationBuilder.DropTable(
                name: "Needs");

            migrationBuilder.DropTable(
                name: "OpportunityScopeItems");

            migrationBuilder.DropTable(
                name: "Parameters");

            migrationBuilder.DropTable(
                name: "ProductPrices");

            migrationBuilder.DropTable(
                name: "ProjectDependencies");

            migrationBuilder.DropTable(
                name: "ProjectEffortDemands");

            migrationBuilder.DropTable(
                name: "ProjectFeatures");

            migrationBuilder.DropTable(
                name: "RoadmapItems");

            migrationBuilder.DropTable(
                name: "SimulationRuns");

            migrationBuilder.DropTable(
                name: "TeamCapacities");

            migrationBuilder.DropTable(
                name: "Approaches");

            migrationBuilder.DropTable(
                name: "Opportunities");

            migrationBuilder.DropTable(
                name: "Features");

            migrationBuilder.DropTable(
                name: "Projects");

            migrationBuilder.DropTable(
                name: "AssumptionSets");

            migrationBuilder.DropTable(
                name: "Roadmaps");

            migrationBuilder.DropTable(
                name: "Teams");

            migrationBuilder.DropTable(
                name: "BusinessProcesses");

            migrationBuilder.DropTable(
                name: "Channels");

            migrationBuilder.DropTable(
                name: "Firms");

            migrationBuilder.DropTable(
                name: "Products");

            migrationBuilder.DropTable(
                name: "Initiatives");

            migrationBuilder.DropTable(
                name: "ClientSegments");

            migrationBuilder.DropTable(
                name: "Competitors");

            migrationBuilder.DropTable(
                name: "Markets");

            migrationBuilder.DropTable(
                name: "Workspaces");
        }
    }
}
