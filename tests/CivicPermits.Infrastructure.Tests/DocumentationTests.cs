using CivicPermits.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace CivicPermits.Infrastructure.Tests;

/// <summary>Keeps the data diagram in the documentation from drifting away from the real model.</summary>
public class DocumentationTests
{
    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root (the folder with global.json) was not found.");
    }

    [Fact]
    public async Task Every_permit_table_in_the_model_appears_in_the_architecture_data_diagram_and_the_reverse()
    {
        using SqliteTestDatabase database = await SqliteTestDatabase.MigratedAsync();
        await using CivicPermitsDbContext context = database.CreateContext();

        string[] permitTables = [.. context.Model.GetEntityTypes()
            .Select(type => type.GetTableName())
            .OfType<string>()
            .Where(table => !table.StartsWith("AspNet", StringComparison.Ordinal))
            .Distinct()
            .Order(StringComparer.Ordinal)];

        string document = File.ReadAllText(Path.Combine(RepositoryRoot(), "docs", "ARCHITECTURE.md"));
        int start = document.IndexOf("erDiagram", StringComparison.Ordinal);
        string diagram = document[start..document.IndexOf("```", start, StringComparison.Ordinal)];

        Assert.All(permitTables, table => Assert.Contains(table, diagram, StringComparison.Ordinal));

        // The reverse: every table the diagram draws exists in the model.
        string[] drawn = [.. System.Text.RegularExpressions.Regex.Matches(diagram, @"^    (\w+) \{", System.Text.RegularExpressions.RegexOptions.Multiline, TimeSpan.FromSeconds(1))
            .Select(match => match.Groups[1].Value)
            .Order(StringComparer.Ordinal)];

        Assert.Equal(permitTables, drawn);
    }
}
