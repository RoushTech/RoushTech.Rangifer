namespace RoushTech.Rangifer.Cli;

using System.CommandLine;
using RoushTech.Rangifer.EntityFrameworkCore.Design;

internal static class Program
{
    public static int Main(string[] args)
    {
        var projectOption = new Option<DirectoryInfo>("--project", "-p")
        {
            Description = "Project directory that holds the migrations. Defaults to the current directory.",
        };
        var contextOption = new Option<string>("--context", "-c")
        {
            Description = "Only convert migrations whose [DbContext(typeof(...))] names this type.",
        };
        var dryRunOption = new Option<bool>("--dry-run")
        {
            Description = "Check and report the conversion without writing any files.",
        };

        var convert = new Command("convert", "Convert full migration designers into model deltas.")
        {
            projectOption,
            contextOption,
            dryRunOption,
        };
        convert.SetAction(parseResult =>
        {
            var project = parseResult.GetValue(projectOption) ?? new DirectoryInfo(Environment.CurrentDirectory);
            var dryRun = parseResult.GetValue(dryRunOption);
            var summary = ThinDesignerConverter.Convert(
                DesignerSet.Scan(project.FullName),
                parseResult.GetValue(contextOption),
                dryRun,
                Console.WriteLine);

            Console.WriteLine(
                $"{(dryRun ? "Would convert" : "Converted")} {summary.Converted} designers "
                + $"({summary.CharactersBefore:N0} to {summary.CharactersAfter:N0} characters). "
                + $"{summary.Updated} updated to the current format, {summary.AlreadyThin} already thin, {summary.WithoutModel} without a target model.");
            return 0;
        });

        var ef = new Command("ef", "Entity Framework Core migration tools.") { convert };
        var root = new RootCommand("Rangifer, RoushTech's .NET tools.") { ef };
        return root.Parse(args).Invoke();
    }
}
