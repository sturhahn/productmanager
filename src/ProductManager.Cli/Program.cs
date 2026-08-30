using System.CommandLine;
using ProductManager.Cli.Commands;

var root = new RootCommand("pm — command-line client for the ProductManager API.");
root.Options.Add(CliInfrastructure.BaseUrlOption);

root.Subcommands.Add(AuthCommands.Build());
root.Subcommands.Add(WorkspaceCommands.Build());
root.Subcommands.Add(TeamCommands.Build());
root.Subcommands.Add(InitiativeCommands.Build());
root.Subcommands.Add(ProjectCommands.Build());
root.Subcommands.Add(FeatureCommands.Build());
root.Subcommands.Add(RoadmapCommands.Build());
root.Subcommands.Add(SegmentCommands.Build());
root.Subcommands.Add(FirmCommands.Build());
root.Subcommands.Add(NeedCommands.Build());
root.Subcommands.Add(MarketEventCommands.Build());
root.Subcommands.Add(CovariateCommands.Build());
root.Subcommands.Add(CoverageCommands.Build());
root.Subcommands.Add(AssumptionSetCommands.Build());
root.Subcommands.Add(ChannelCommands.Build());
root.Subcommands.Add(IntegrationCommands.Build());
root.Subcommands.Add(FirmRelationshipCommands.Build());
root.Subcommands.Add(OpportunityCommands.Build());
root.Subcommands.Add(CalibrationCommands.Build());
root.Subcommands.Add(SimulationCommands.Build());
root.Subcommands.Add(SeedCommand.Build());

return await root.Parse(args).InvokeAsync();
