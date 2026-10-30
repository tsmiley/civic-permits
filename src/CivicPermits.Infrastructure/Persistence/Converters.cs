using CivicPermits.Domain.Common;
using CivicPermits.Domain.Permits;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;

namespace CivicPermits.Infrastructure.Persistence;

/// <summary>
/// Stores an amount as a whole number of cents. Integers add, compare and sort exactly on every provider, which
/// SQLite's floating-point storage of decimals does not.
/// </summary>
internal sealed class MoneyConverter()
    : ValueConverter<Money, long>(money => money.Cents, cents => new Money(cents));

/// <summary>Stores a permit number in its printed form, <c>CP-2026-000042</c>.</summary>
internal sealed class PermitNumberConverter()
    : ValueConverter<PermitNumber, string>(number => number.ToString(), text => ParseStored(text))
{
    private static PermitNumber ParseStored(string text) =>
        PermitNumber.TryParse(text, out PermitNumber number)
            ? number
            : throw new InvalidOperationException("The database holds a permit number that is not in the expected format.");
}

/// <summary>
/// Stores a moment as UTC. SQLite keeps no time zone, and text that carries an offset does not sort by the instant
/// it means, so every moment is converted to UTC on the way in and read back as UTC with a zero offset.
/// </summary>
internal sealed class UtcDateTimeOffsetConverter()
    : ValueConverter<DateTimeOffset, DateTime>(
        moment => moment.UtcDateTime,
        stored => new DateTimeOffset(DateTime.SpecifyKind(stored, DateTimeKind.Utc)));
