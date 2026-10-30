using System.Text.Json;

if (args.Length == 0 || args[0] is "--help" or "help")
{
    Console.WriteLine("pmcro | ProjectName development CLI");
    Console.WriteLine("  pmcro status  - display current local admission policy");
    Console.WriteLine("  pmcro cycle   - show protected cycle entrypoint (never executes)");
    return;
}
if (args[0] == "status")
{
    Console.WriteLine(JsonSerializer.Serialize(new {
        project = "ProjectName", target = "net10.0",
        registered = false, productionEnabled = false,
        workersAdmitted = 0, checkerApproved = false
    }));
    return;
}
if (args[0] == "cycle")
{
    Console.Error.WriteLine("HALT: NO_EFFECTIVE_GRANT; no production execution.");
    Environment.ExitCode = 3;
    return;
}
Console.Error.WriteLine("Unknown command; use --help.");
Environment.ExitCode = 2;
