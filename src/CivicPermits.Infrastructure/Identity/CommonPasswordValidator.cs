using System.Globalization;
using CivicPermits.Infrastructure.Persistence;
using Microsoft.AspNetCore.Identity;

namespace CivicPermits.Infrastructure.Identity;

/// <summary>
/// Refuses a new password that is on a bundled list of the passwords people guess first, or is made only of them. NIST
/// SP 800-63B-4 asks for length and a blocklist instead of rules about which kinds of character to include. The list
/// is part of the program: no network call is made, and no password or part of one leaves the host.
/// </summary>
/// <remarks>
/// This is not a breached-password corpus. It catches the stems everyone tries (<c>password</c>, <c>qwerty</c>,
/// <c>letmein</c>, a first name) in the usual disguises: other capitals, spaces and dashes, <c>p@ssw0rd</c>-style
/// swaps, a tail of digits or symbols, a stem repeated, up to three stems joined, a single repeated or counting run,
/// and a password that contains the account's own name. A deployment that wants more should add a breached-password
/// service as a second validator; it would need the network, which is why this one does not.
/// </remarks>
public sealed class CommonPasswordValidator : IPasswordValidator<ApplicationUser>
{
    /// <summary>The most characters a new password may have; far above the 64 that standards ask to be accepted.</summary>
    public const int MaximumLength = 256;

    private const string TooCommon = "PasswordTooCommon";
    private const string TooLong = "PasswordTooLong";
    private const int LongestStems = 3;
    private const int ShortestStem = 4;

    private static readonly HashSet<string> Stems = new(CommonPasswords.Stems, StringComparer.Ordinal);

    /// <inheritdoc />
    public Task<IdentityResult> ValidateAsync(UserManager<ApplicationUser> manager, ApplicationUser user, string? password)
    {
        ArgumentNullException.ThrowIfNull(user);

        if (string.IsNullOrEmpty(password))
        {
            return Task.FromResult(IdentityResult.Success); // Identity's own length rule reports an empty password.
        }

        if (password.Length > MaximumLength)
        {
            return Task.FromResult(IdentityResult.Failed(new IdentityError
            {
                Code = TooLong,
                Description = string.Create(CultureInfo.InvariantCulture, $"Use at most {MaximumLength} characters."),
            }));
        }

        return Task.FromResult(IsGuessable(password, user)
            ? IdentityResult.Failed(new IdentityError
            {
                Code = TooCommon,
                Description = "That password is one of the first that people guess. Choose another; a sentence only you would write works well.",
            })
            : IdentityResult.Success);
    }

    /// <summary>Whether the password is on the list, made of it, a plain run, or built from the account's own name.</summary>
    public static bool IsGuessable(string password, ApplicationUser? owner = null)
    {
        ArgumentNullException.ThrowIfNull(password);

        string plain = Simplify(password, swapLookalikes: false);
        string swapped = Simplify(password, swapLookalikes: true);

        return IsRun(plain)
            || IsKeyboardWalk(plain)
            || IsMadeOfStems(plain, swapped)
            || ContainsAccountName(plain, owner)
            || ContainsAccountName(swapped, owner);
    }

    /// <summary>Lower case, with spaces, dashes, dots and underscores removed, and optionally the usual look-alike swaps undone.</summary>
    private static string Simplify(string password, bool swapLookalikes)
    {
        char[] kept = new char[password.Length];
        int length = 0;

        foreach (char raw in password)
        {
            char c = char.ToLowerInvariant(raw);

            if (c is ' ' or '-' or '_' or '.' or '\t')
            {
                continue;
            }

            kept[length++] = swapLookalikes ? Unswap(c) : c;
        }

        return new string(kept, 0, length);
    }

    private static char Unswap(char c) => c switch
    {
        '0' => 'o',
        '1' => 'i',
        '3' => 'e',
        '4' => 'a',
        '5' => 's',
        '7' => 't',
        '@' => 'a',
        '$' => 's',
        '!' => 'i',
        _ => c,
    };

    /// <summary>One character over and over, or a straight run up or down such as 123456789012345 or abcdefghijklmno.</summary>
    private static bool IsRun(string text)
    {
        if (text.Length < 4)
        {
            return false;
        }

        if (text.All(c => c == text[0]))
        {
            return true;
        }

        bool up = true;
        bool down = true;

        for (int i = 1; i < text.Length; i++)
        {
            int step = Step(text[i - 1], text[i]);
            up &= step == 1;
            down &= step == -1;
        }

        return up || down;
    }

    // Steps are counted within digits and within letters, wrapping 9 to 0 and z to a; anything else breaks the run.
    private static int Step(char from, char to)
    {
        if (char.IsAsciiDigit(from) && char.IsAsciiDigit(to))
        {
            return ((to - from + 10) % 10) switch { 1 => 1, 9 => -1, _ => 0 };
        }

        if (char.IsAsciiLetterLower(from) && char.IsAsciiLetterLower(to))
        {
            return ((to - from + 26) % 26) switch { 1 => 1, 25 => -1, _ => 0 };
        }

        return 0;
    }

    // Where each key sits on a US keyboard, in key widths: the number row, then the three letter rows, each shifted right
    // by the width of the key at its left edge (Tab, Caps Lock, Shift), so q sits between 1 and 2 and a between q and w.
    private static readonly Dictionary<char, (double X, double Y)> Keys = BuildKeys();

    private const int ShortestWalk = 8;
    private const double LongestStep = 1.45;
    private const double WalkShare = 0.75;

    private static Dictionary<char, (double X, double Y)> BuildKeys()
    {
        Dictionary<char, (double X, double Y)> keys = [];
        (string Row, double Left)[] rows = [("1234567890", 1.5), ("qwertyuiop", 2.0), ("asdfghjkl", 2.25), ("zxcvbnm", 2.75)];

        for (int y = 0; y < rows.Length; y++)
        {
            for (int i = 0; i < rows[y].Row.Length; i++)
            {
                keys[rows[y].Row[i]] = (rows[y].Left + i, y);
            }
        }

        return keys;
    }

    /// <summary>
    /// A path across neighbouring keys, in any direction: along a row (qwerty), down a column (1q2w3e4r), up a diagonal
    /// (zaq12wsx). Famous breached passwords are mostly such shapes. It is refused when at least eight characters in a row
    /// are on neighbouring keys and they make up three quarters of the password's letters and digits.
    /// </summary>
    private static bool IsKeyboardWalk(string text)
    {
        char[] mapped = [.. text.Where(Keys.ContainsKey)];

        if (mapped.Length < ShortestWalk)
        {
            return false;
        }

        int longest = 1;
        int run = 1;

        for (int i = 1; i < mapped.Length; i++)
        {
            (double x, double y) from = Keys[mapped[i - 1]];
            (double x, double y) to = Keys[mapped[i]];
            double distance = Math.Sqrt(Math.Pow(to.x - from.x, 2) + Math.Pow(to.y - from.y, 2));

            run = distance <= LongestStep ? run + 1 : 1;
            longest = Math.Max(longest, run);
        }

        return longest >= ShortestWalk && longest >= mapped.Length * WalkShare;
    }

    /// <summary>
    /// Whether the whole password is up to three stems from the list with digits and symbols between and after them, so
    /// <c>password123456789</c>, <c>passwordpassword</c> and <c>dragonmonkey2026!</c> are refused and a sentence is not.
    /// </summary>
    private static bool IsMadeOfStems(string plain, string swapped)
    {
        // Both strings have the same length, character for character: the second has the look-alikes undone. A character
        // that is not a letter may be filler (a digit or symbol after or between stems) or, through its look-alike, part
        // of a stem, so p@ssw0rd123 reads as password then 123. reachable[i]: the first i characters are stems and filler.
        bool[] reachable = new bool[plain.Length + 1];
        int[] used = new int[plain.Length + 1];
        reachable[0] = true;

        for (int start = 0; start < plain.Length; start++)
        {
            if (!reachable[start])
            {
                continue;
            }

            if (!char.IsAsciiLetter(plain[start]))
            {
                Mark(reachable, used, start + 1, used[start]);
            }

            if (used[start] >= LongestStems || !char.IsAsciiLetter(swapped[start]))
            {
                continue;
            }

            for (int end = start + ShortestStem; end <= Math.Min(plain.Length, start + CommonPasswords.LongestStem); end++)
            {
                if (Stems.Contains(swapped[start..end]))
                {
                    Mark(reachable, used, end, used[start] + 1);
                }
            }
        }

        return reachable[plain.Length] && used[plain.Length] > 0;
    }

    private static void Mark(bool[] reachable, int[] used, int at, int stems)
    {
        if (!reachable[at] || stems < used[at])
        {
            reachable[at] = true;
            used[at] = stems;
        }
    }

    private static bool ContainsAccountName(string text, ApplicationUser? owner)
    {
        if (owner?.UserName is not { Length: > 0 } name)
        {
            return false;
        }

        string local = Simplify(name.Split('@')[0], swapLookalikes: false);

        return local.Length >= 4 && text.Contains(local, StringComparison.Ordinal);
    }
}
