using CivicPermits.Domain.Permits;

namespace CivicPermits.Domain.Tests;

public class PermitNumberTests
{
    [Fact]
    public void A_number_is_written_with_the_prefix_the_year_and_a_zero_padded_sequence()
    {
        PermitNumber number = PermitNumber.From(2026, 42);

        Assert.Equal("CP-2026-000042", number.ToString());
    }

    [Theory]
    [InlineData("CP-2026-000042", 2026, 42)]
    [InlineData("CP-2000-000001", 2000, 1)]
    [InlineData("CP-9999-999999", 9999, 999_999)]
    public void Text_in_the_exact_format_is_read_back_into_its_parts(string text, int year, int sequence)
    {
        bool parsed = PermitNumber.TryParse(text, out PermitNumber number);

        Assert.True(parsed);
        Assert.Equal(year, number.Year);
        Assert.Equal(sequence, number.Sequence);
        Assert.Equal(text, number.ToString());
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData(" ")]
    [InlineData("cp-2026-000042")]        // wrong case: rejected, not "fixed"
    [InlineData(" CP-2026-000042")]       // leading space
    [InlineData("CP-2026-000042\n")]      // trailing newline: the regex end anchor must not forgive it
    [InlineData("CP-2026-42")]            // sequence too short
    [InlineData("CP-2026-0000042")]       // sequence too long
    [InlineData("CP-26-000042")]          // year too short
    [InlineData("CP-1999-000042")]        // before the first possible year
    [InlineData("CP-2026-000000")]        // sequence starts at one
    [InlineData("CP-2026-00004\u0662")]   // an Arabic-Indic digit: ASCII digits only
    [InlineData("XX-2026-000042")]
    public void Anything_else_is_rejected_without_throwing(string? text)
    {
        bool parsed = PermitNumber.TryParse(text, out PermitNumber number);

        Assert.False(parsed);
        Assert.Equal(default, number);
    }

    [Theory]
    [InlineData(1999, 1)]
    [InlineData(2026, 0)]
    [InlineData(2026, 1_000_000)]
    [InlineData(10_000, 1)]
    public void A_part_out_of_range_cannot_be_built(int year, int sequence)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => PermitNumber.From(year, sequence));
    }
}
