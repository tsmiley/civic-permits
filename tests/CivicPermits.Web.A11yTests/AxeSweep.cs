using System.Text;
using Deque.AxeCore.Commons;
using Deque.AxeCore.Playwright;
using Microsoft.Playwright;

namespace CivicPermits.Web.A11yTests;

/// <summary>
/// Runs the axe-core engine over a page in a given state and collects what it finds, so one test can look at many
/// states and report every violation together instead of stopping at the first.
/// </summary>
/// <remarks>
/// The rules run are WCAG 2.0 and 2.1 level A and AA plus axe's best-practice rules. axe-core is used unmodified,
/// through the Deque.AxeCore.Playwright package (see NOTICE for its license). Automated rules find only a part of
/// what can go wrong; the manual procedure in docs/ACCESSIBILITY_TESTING.md covers the rest.
/// </remarks>
internal sealed class AxeSweep
{
    private static readonly AxeRunOptions Options = new()
    {
        RunOnly = new RunOnlyOptions
        {
            Type = "tag",
            Values = ["wcag2a", "wcag2aa", "wcag21a", "wcag21aa", "best-practice"],
        },
    };

    private readonly List<string> _violations = [];
    private readonly List<string> _states = [];

    /// <summary>The version of the axe-core engine that ran, as it reports itself.</summary>
    public static string? EngineVersion { get; private set; }

    /// <summary>How many states have been scanned.</summary>
    public int StatesScanned => _states.Count;

    /// <summary>The names of the states scanned, in order.</summary>
    public IReadOnlyList<string> States => _states;

    /// <summary>Every violation found, one formatted entry each.</summary>
    public IReadOnlyList<string> Violations => _violations;

    /// <summary>Scans the page as it is now and records the result under the state's name.</summary>
    public async Task ScanAsync(IPage page, string state)
    {
        AxeResult result = await page.RunAxe(Options);
        EngineVersion = result.TestEngine?.Version;
        _states.Add(state);

        foreach (AxeResultItem violation in result.Violations)
        {
            StringBuilder entry = new();
            entry.Append(CultureInvariant($"[{state}] {violation.Id} ({violation.Impact}): {violation.Help}"));

            foreach (AxeResultNode node in violation.Nodes.Take(3))
            {
                string html = node.Html.Length > 160 ? node.Html[..160] + "..." : node.Html;
                entry.Append("\n      ").Append(node.Target).Append(" => ").Append(html);
            }

            _violations.Add(entry.ToString());
        }
    }

    /// <summary>Fails with the whole list when any state had a violation.</summary>
    public void AssertClean() =>
        Assert.True(_violations.Count == 0, $"{_violations.Count} accessibility violation(s) over {_states.Count} states:\n" + string.Join("\n", _violations));

    private static string CultureInvariant(FormattableString text) => FormattableString.Invariant(text);
}
