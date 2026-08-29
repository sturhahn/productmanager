using System.CommandLine;
using ProductManager.Cli.Config;

namespace ProductManager.Cli.Commands;

internal static class AuthCommands
{
    public static Command Build()
    {
        var auth = new Command("auth", "Sign in to the ProductManager API and manage the stored session.");
        auth.Subcommands.Add(BuildLogin());
        auth.Subcommands.Add(BuildWhoAmI());
        return auth;
    }

    private static Command BuildLogin()
    {
        var passwordOption = new Option<string>("--password") { Description = "The single-user API password.", Required = true };
        var login = new Command("login", "Exchanges the API password for a bearer token and stores it in ~/.productmanager/config.json.");
        login.Options.Add(passwordOption);

        login.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async ct2 =>
        {
            var (client, baseUrl) = CliInfrastructure.BuildAnonymousClient(parseResult);
            var token = await client.IssueTokenAsync(parseResult.GetValue(passwordOption)!, ct2);

            ConfigStore.Save(new CliConfig
            {
                BaseUrl = baseUrl,
                AccessToken = token.AccessToken,
                ExpiresAt = token.ExpiresAt
            });

            Console.WriteLine($"Signed in against {baseUrl}. Token expires {token.ExpiresAt:u}.");
        }, ct));

        return login;
    }

    private static Command BuildWhoAmI()
    {
        var whoami = new Command("whoami", "Shows the configured API and whether the stored token is still usable.");
        whoami.SetAction((parseResult, ct) => CliInfrastructure.RunAsync(async _ =>
        {
            var config = ConfigStore.Load();
            CliInfrastructure.WriteJson(new
            {
                baseUrl = config.BaseUrl,
                signedIn = config.HasUsableToken,
                expiresAt = config.ExpiresAt
            });
            await Task.CompletedTask;
        }, ct));

        return whoami;
    }
}
