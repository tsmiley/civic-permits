using System.Globalization;
using System.Text;
using CivicPermits.Domain.Applications;

namespace CivicPermits.Domain.Tests;

/// <summary>
/// Diagrams in the documentation that are drawn from the code, so they cannot drift from it. When the workflow
/// table changes, these tests fail until the documentation is regenerated:
/// <c>CP_UPDATE_DOCS=1 dotnet test tests/CivicPermits.Domain.Tests --filter DocumentationTests</c>.
/// </summary>
public class DocumentationTests
{
    private const string Start = "<!-- workflow-diagram:start -->";
    private const string End = "<!-- workflow-diagram:end -->";

    private static bool Updating => Environment.GetEnvironmentVariable("CP_UPDATE_DOCS") == "1";

    private static string RepositoryRoot()
    {
        DirectoryInfo? directory = new(AppContext.BaseDirectory);

        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "global.json")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName ?? throw new InvalidOperationException("The repository root (the folder with global.json) was not found.");
    }

    /// <summary>The Mermaid state diagram of the workflow table, one edge per allowed step, labelled with who may take it.</summary>
    internal static string WorkflowDiagram()
    {
        StringBuilder diagram = new();
        diagram.Append("stateDiagram-v2\n");
        diagram.Append("    [*] --> Draft\n");

        foreach (WorkflowTransition step in ApplicationWorkflow.Transitions)
        {
            string who = string.Join(" or ", step.Roles.Select(role => role.ToString()));
            diagram.Append(CultureInfo.InvariantCulture, $"    {step.From} --> {step.To}: {step.Action} ({who})\n");
        }

        foreach (ApplicationStatus status in Enum.GetValues<ApplicationStatus>().Where(ApplicationWorkflow.IsFinal))
        {
            diagram.Append(CultureInfo.InvariantCulture, $"    {status} --> [*]\n");
        }

        return diagram.ToString();
    }

    private static string Read(string path) => File.ReadAllText(path).Replace("\r\n", "\n", StringComparison.Ordinal);

    /// <summary>GitHub's anchor for a heading: lower case, punctuation dropped, spaces to hyphens.</summary>
    private static string Slug(string heading)
    {
        StringBuilder slug = new();

        foreach (char c in heading.Trim().ToLowerInvariant())
        {
            if (char.IsLetterOrDigit(c) || c == '-')
            {
                slug.Append(c);
            }
            else if (c == ' ')
            {
                slug.Append('-');
            }
        }

        return slug.ToString();
    }

    [Fact]
    public void Every_relative_link_in_the_documentation_goes_somewhere_that_exists()
    {
        string root = RepositoryRoot();
        List<string> broken = [];
        int checkedLinks = 0;

        IEnumerable<string> documents = Directory.EnumerateFiles(root, "*.md", SearchOption.AllDirectories)
            .Where(path => !path.Contains($"{Path.DirectorySeparatorChar}bin{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}obj{Path.DirectorySeparatorChar}", StringComparison.Ordinal)
                && !path.Contains($"{Path.DirectorySeparatorChar}.git{Path.DirectorySeparatorChar}", StringComparison.Ordinal));

        foreach (string document in documents)
        {
            // Inline links and images: [text](target) and ![alt](target). Fenced code is skipped.
            string text = System.Text.RegularExpressions.Regex.Replace(
                Read(document), "```.*?```", string.Empty, System.Text.RegularExpressions.RegexOptions.Singleline, TimeSpan.FromSeconds(2));

            foreach (System.Text.RegularExpressions.Match match in System.Text.RegularExpressions.Regex.Matches(
                         text, @"\]\(([^)\s]+)\)", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(2)))
            {
                string target = match.Groups[1].Value;

                if (target.StartsWith("http", StringComparison.OrdinalIgnoreCase) || target.StartsWith("mailto:", StringComparison.OrdinalIgnoreCase))
                {
                    continue;
                }

                checkedLinks++;
                string[] parts = target.Split('#', 2);
                string file = parts[0].Length == 0 ? document : Path.GetFullPath(Path.Combine(Path.GetDirectoryName(document)!, parts[0]));

                if (!File.Exists(file) && !Directory.Exists(file))
                {
                    broken.Add($"{Path.GetRelativePath(root, document)}: {target} (no such file)");
                }
                else if (parts.Length == 2 && file.EndsWith(".md", StringComparison.Ordinal)
                    && !Read(file).Split('\n').Any(line => line.StartsWith('#') && Slug(line.TrimStart('#')) == parts[1]))
                {
                    broken.Add($"{Path.GetRelativePath(root, document)}: {target} (no such heading)");
                }
            }
        }

        Assert.True(checkedLinks > 30, $"Only {checkedLinks} links were checked; the check is not looking in the right place.");
        Assert.Empty(broken);
    }

    [Fact]
    public void The_workflow_diagram_file_is_the_one_the_code_draws()
    {
        string path = Path.Combine(RepositoryRoot(), "docs", "diagrams", "application-workflow.mmd");
        string expected = WorkflowDiagram();

        if (Updating)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllText(path, expected, new UTF8Encoding(false));
        }

        Assert.True(File.Exists(path), $"{path} does not exist. Regenerate it with CP_UPDATE_DOCS=1.");
        Assert.Equal(expected, Read(path));
    }

    [Fact]
    public void The_diagram_in_the_architecture_document_is_the_one_the_code_draws()
    {
        string path = Path.Combine(RepositoryRoot(), "docs", "ARCHITECTURE.md");
        string document = Read(path);
        int start = document.IndexOf(Start, StringComparison.Ordinal);
        int end = document.IndexOf(End, StringComparison.Ordinal);

        Assert.True(start >= 0 && end > start, $"ARCHITECTURE.md must contain {Start} and {End} around the diagram.");

        string block = $"{Start}\n```mermaid\n{WorkflowDiagram()}```\n{End}";

        if (Updating)
        {
            File.WriteAllText(path, document[..start] + block + document[(end + End.Length)..], new UTF8Encoding(false));
            document = Read(path);
            start = document.IndexOf(Start, StringComparison.Ordinal);
            end = document.IndexOf(End, StringComparison.Ordinal);
        }

        Assert.Equal(block, document[start..(end + End.Length)]);
    }
}
