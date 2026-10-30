using Microsoft.Extensions.Options;

namespace CivicPermits.Infrastructure.Persistence;

/// <summary>Rejects database settings that could only fail later and less clearly.</summary>
internal sealed class DatabaseOptionsValidator : IValidateOptions<DatabaseOptions>
{
    public ValidateOptionsResult Validate(string? name, DatabaseOptions options)
    {
        List<string> failures = [];

        if (!Enum.IsDefined(options.Provider))
        {
            failures.Add($"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.Provider)} must be one of: {string.Join(", ", Enum.GetNames<DatabaseProvider>())}.");
        }

        if (string.IsNullOrWhiteSpace(options.ConnectionString))
        {
            failures.Add($"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.ConnectionString)} is required.");
        }

        if (options.SeedSampleData && !options.MigrateOnStartup)
        {
            failures.Add($"{DatabaseOptions.SectionName}:{nameof(DatabaseOptions.SeedSampleData)} needs {DatabaseOptions.SectionName}:{nameof(DatabaseOptions.MigrateOnStartup)}, because the tables must exist first.");
        }

        return failures.Count == 0 ? ValidateOptionsResult.Success : ValidateOptionsResult.Fail(failures);
    }
}
