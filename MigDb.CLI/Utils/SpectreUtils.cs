using MigDb.Core.Migration;
using MigDb.Core.Migration.Validation;
using MigDb.Core.Schema.Programmable;
using Spectre.Console;
using System.Reflection;

namespace MigDb.CLI.Utils;

internal static class SpectreUtils
{
    public static IAnsiConsole Error { get; } = AnsiConsole.Create(new AnsiConsoleSettings
    {
        Out = new AnsiConsoleOutput(Console.Error)
    });

    /// <summary>
    /// Renders validation failures as a tree so each error is attributed to the directory
    /// or file it came from. Without this the detail only ever reaches the log file
    /// </summary>
    /// <param name="validations">The failing results to render</param>
    public static void WriteValidationFailures(IReadOnlyList<MigrationValidationResult> validations)
    {
        Tree tree = new("[red]Validation errors[/]");

        foreach (MigrationValidationResult v in validations)
            AddValidationNode(tree.AddNode(FormatSubject(v)), v);

        Error.Write(tree);
        Error.WriteLine();
    }

    private static void AddValidationNode(TreeNode node, MigrationValidationResult result)
    {
        foreach (MigrationValidationError e in result.Errors.Distinct())
            node.AddNode($"[yellow]{Markup.Escape(e.Describe())}[/]");

        foreach (MigrationValidationResult child in result.Children)
            AddValidationNode(node.AddNode(FormatSubject(child)), child);
    }

    public static string DescribeExclusion(SchemaProgrammableExclusion exclusion)
    {
        if (exclusion == SchemaProgrammableExclusion.SchemaBound)
            return "Schema-bound";

        if (exclusion == SchemaProgrammableExclusion.IndexedView)
            return "Indexed view";

        return exclusion.ToString();
    }

    private static string FormatSubject(MigrationValidationResult result)
    {
        string subject = string.IsNullOrWhiteSpace(result.Subject) ? "(unknown)" : result.Subject;

        return $"[red]{Markup.Escape(subject)}[/]";
    }

    private static string CustomFontPath => Path.Combine(AppContext.BaseDirectory, "Resources", "Fonts");

    private static Color[] TitleColors => [
        Color.DeepSkyBlue3,
        Color.Magenta1,
        Color.OrangeRed1,
        Color.PaleGreen1,
        Color.Plum1,
        Color.RoyalBlue1,
        Color.SteelBlue1,
        Color.Gray62,
        Color.Gold1,
        Color.DarkSeaGreen4_1,
        Color.Cyan1,
        Color.Chartreuse4
    ];

    public static string Prompt()
    {
        return AnsiConsole.Prompt(new TextPrompt<string>("[green]>[/]").AllowEmpty());
    }

    public static Rule CreateSectionRule(string? message = null)
    {
        if (message is null)
        {
            return new Rule()
             .RuleStyle(new Style(Color.Purple))
             .AsciiBorder();
        }
        else
        {
            return new Rule(message)
                .RuleStyle(new Style(Color.Purple))
                .AsciiBorder();
        }
    }

    public static void WriteHeaderRule(string message)
    {
        AnsiConsole.Write(CreateSectionRule(message));
        AnsiConsole.Write(CreateSectionRule());
    }

    public static void WriteSectionRule()
    {
        Rule rule = CreateSectionRule();
        AnsiConsole.Write(rule);
    }

    public static void WriteTitle()
    {
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();

        Assembly assm = typeof(Program).Assembly;
        string assemblyName = assm.GetName().Name ?? "Forgot My Name";
        string assemblyVersion = $"{assm.GetName().Version?.Major}.{assm.GetName().Version?.Minor}.{assm.GetName().Version?.Build}";

        FigletText titleText = new FigletText(GetRandomFigletFont(), assemblyName).Centered().Color(GetRandomFigletColor());

        Rule rule = new Rule("[red]Rod & Devs only. Keep Out.[/]")
            .RuleStyle(Style.Parse("olive"));

        Text version = new(assemblyVersion, new Style(Color.Red, decoration: Decoration.Dim))
        {
            Justification = Justify.Center
        };

        AnsiConsole.Write(titleText);
        AnsiConsole.Write(rule);
        AnsiConsole.Write(version);
        AnsiConsole.WriteLine();
        AnsiConsole.WriteLine();
    }

    public static FigletFont GetRandomFigletFont()
    {
        string fontsDirectory = CustomFontPath;

        if (!Directory.Exists(fontsDirectory))
            return FigletFont.Default;

        string[] fontFiles = Directory.GetFiles(fontsDirectory, "*.flf");

        if (fontFiles.Length == 0)
            return FigletFont.Default;

        string fontFile = fontFiles[Random.Shared.Next(fontFiles.Length)];

        // just in case something goes wrong, like bad font file idk
        try
        {
            return FigletFont.Load(fontFile);
        }
        catch
        {
            return FigletFont.Default;
        }
    }

    public static Color GetRandomFigletColor()
    {
        Color color = TitleColors[Random.Shared.Next(TitleColors.Length)];

        return color;
    }

    public static void SetColorSupport()
    {
        SetColorSupport(AnsiConsole.Console);
        SetColorSupport(Error);
    }

    public static void SetColorSupport(IAnsiConsole console)
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
            return;

        console.Profile.Capabilities.ColorSystem = ColorSystem.NoColors;
        console.Profile.Capabilities.Ansi = false;
    }

    public static void WriteError(string message)
    {
        Error.MarkupLineInterpolated($"[red]{message}[/]");
    }

    public static void WriteConfigKeys(IReadOnlyList<string> keys)
    {
        Tree tree = new("[grey]Known keys[/]");

        foreach (string key in keys)
            tree.AddNode($"[grey]{Markup.Escape(key)}[/]");

        Error.Write(tree);
        Error.WriteLine();
    }
}
