namespace ForexAlert.Configuration;

public sealed record CommandLineOptions(
    bool DryRun,
    bool ValidateConfiguration,
    string? SimulationFixture,
    bool RunOnce,
    bool ShowHelp)
{
    public static CommandLineOptions Parse(IReadOnlyList<string> arguments)
    {
        bool dryRun = false;
        bool validate = false;
        bool runOnce = false;
        bool help = false;
        string? simulation = null;

        for (int index = 0; index < arguments.Count; index++)
        {
            switch (arguments[index])
            {
                case "--dry-run":
                    dryRun = true;
                    break;
                case "--validate-config":
                    validate = true;
                    break;
                case "--once":
                    runOnce = true;
                    break;
                case "--help" or "-h":
                    help = true;
                    break;
                case "--simulate":
                    if (++index >= arguments.Count || arguments[index].StartsWith("--", StringComparison.Ordinal))
                    {
                        throw new ArgumentException("--simulate requires a fixture path or bundled fixture name.");
                    }

                    simulation = arguments[index];
                    break;
                default:
                    throw new ArgumentException($"Unknown command-line option '{arguments[index]}'. Use --help for usage.");
            }
        }

        int terminalCommands = (validate ? 1 : 0) + (simulation is null ? 0 : 1) + (help ? 1 : 0);
        if (terminalCommands > 1)
        {
            throw new ArgumentException("Choose only one of --validate-config, --simulate, or --help.");
        }

        return new CommandLineOptions(dryRun, validate, simulation, runOnce, help);
    }

    public static string HelpText =>
        """
        ForexAlert - alert-only foreign-exchange monitoring

        Usage:
          ForexAlert [--dry-run] [--once]
          ForexAlert --validate-config
          ForexAlert --simulate <fixture>
          ForexAlert --help

        Options:
          --dry-run         Suppress all external notifications.
          --validate-config Validate configuration without connecting to IBKR.
          --simulate PATH   Run the complete alert pipeline with a safe JSON fixture.
          --once            Connect, load initial data, evaluate once, and exit.
          --help, -h        Show this help.
        """;
}
