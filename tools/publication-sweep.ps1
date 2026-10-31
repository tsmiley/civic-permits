<#
.SYNOPSIS
    The publication sweep: looks for secrets, personal details and private names in every commit of the history and in
    every file of the working tree, and checks who the commits are by.

.DESCRIPTION
    Run before a repository is made public and whenever a release is made. It fails (exit code 1) when it finds anything.

    What it looks for, in the text added by every commit on every branch (`git log -p --all`), in every commit message,
    and in every file that is tracked or not ignored:
      - the shapes of credentials: cloud keys, GitHub and Slack tokens, payment keys and webhook secrets, private key
        blocks, JSON web tokens, a bearer token in a header, a password written into a connection string;
      - email addresses and telephone numbers (the fictional `.test` domain and the platform's no-reply addresses are fine);
      - files that should never be committed: databases, certificates, key files, environment files, user-secrets files;
      - private names, from a list kept OUTSIDE the repository (see -NamesFile): publishing the list would publish the names;
      - the author and committer of every commit: only no-reply addresses are allowed.

    It never prints what it matched, only the rule and where (a commit and file, or a file and line), so its output can
    be pasted anywhere. A rule that finds something is fixed in the history (a rewrite, before publication) or in the file.

    -SelfTest proves the sweep can find things: it builds a throwaway repository with one of each planted defect, a secret
    that was added and removed again (history only), and a clean repository, and requires every rule to fire on the first
    and nothing on the second. A sweep that can never fail proves nothing.

.PARAMETER NamesFile
    A text file with one private name per line (lines starting with # are comments). Defaults to the environment variable
    CP_SWEEP_NAMES_FILE. Without one the name check is skipped, and the sweep says so.

.PARAMETER AcceptedFile
    A text file listing findings that are known and accepted, one per line, exactly as the sweep prints them (`rule | where`).
    Defaults to the environment variable CP_SWEEP_ACCEPTED_FILE. It exists for the history of a public repository, which cannot be
    rewritten: a name that was fine when a commit was made and is not now stays in that commit for good. Accepted findings are
    counted and shown as accepted, never hidden; a finding that is not listed still fails. Keep the file outside the repository.

.PARAMETER SelfTest
    Run the self-test instead of sweeping this repository.

.EXAMPLE
    pwsh tools/publication-sweep.ps1 -NamesFile D:\Portfolio\private\sweep-names.txt

.EXAMPLE
    pwsh tools/publication-sweep.ps1 -SelfTest
#>
[CmdletBinding()]
param(
    [string]$NamesFile = $env:CP_SWEEP_NAMES_FILE,
    [string]$AcceptedFile = $env:CP_SWEEP_ACCEPTED_FILE,
    [switch]$SelfTest
)

$ErrorActionPreference = 'Stop'
Set-StrictMode -Version Latest

# ---- The rules ----

$SecretRules = @(
    @{ Id = 'cloud-access-key'; Pattern = 'AKIA[0-9A-Z]{16}' },
    @{ Id = 'github-token'; Pattern = 'gh[pousr]_[A-Za-z0-9]{30,}|github_pat_[A-Za-z0-9_]{30,}' },
    @{ Id = 'payment-key'; Pattern = '[rsp]k_(live|test)_[A-Za-z0-9]{10,}|whsec_[A-Za-z0-9]{10,}' },
    @{ Id = 'private-key-block'; Pattern = '-----BEGIN [A-Z ]*PRIVATE KEY' },
    @{ Id = 'chat-token'; Pattern = 'xox[abprs]-[A-Za-z0-9-]{10,}' },
    @{ Id = 'google-api-key'; Pattern = 'AIza[0-9A-Za-z_-]{35}' },
    @{ Id = 'json-web-token'; Pattern = 'eyJ[A-Za-z0-9_-]{10,}\.eyJ[A-Za-z0-9_-]{10,}\.[A-Za-z0-9_-]{10,}' },
    @{ Id = 'bearer-token-in-header'; Pattern = '(?i)authorization:\s*bearer\s+[A-Za-z0-9._~+/-]{20,}' },
    @{ Id = 'password-in-connection-string'; Pattern = '(?i)\b(password|pwd)=[^;"''\s{}<>$%@]{6,}' }
)

$EmailPattern = '[A-Za-z0-9._%+-]*[A-Za-z0-9]@[A-Za-z0-9-]+(\.[A-Za-z0-9-]+)*\.[A-Za-z]{2,}'
$AllowedEmailDomain = '(?i)(\.test|\.invalid|\.example|example\.(com|org|net)|users\.noreply\.github\.com|^github\.com)$'
$PhonePattern = '(?<![\d.-])(\+?1[ .-])?\(?[2-9]\d{2}\)?[ .-][2-9]\d{2}[ .-]\d{4}(?![\d-])'
$AllowedIdentity = '^[^<]+ <((\d+\+)?[A-Za-z0-9\[\]-]+@users\.noreply\.github\.com|noreply@github\.com)>$'
$ForbiddenFile = '(?i)(\.(db|sqlite|sqlite3|mdf|ldf|pfx|p12|pem|key|jks|snk)$|(^|/)\.env(\.|$)|(^|/)secrets\.json$|(^|/)id_(rsa|ed25519)$|\.user$|(^|/)(bin|obj)/)'
$BinaryExtension = '(?i)\.(png|jpg|jpeg|gif|ico|woff2?|ttf|zip|nupkg|dll|exe|pdf)$'

function Get-Names {
    param([string]$Path)

    if ([string]::IsNullOrWhiteSpace($Path)) { return @() }
    if (-not (Test-Path -LiteralPath $Path)) { throw "The names file '$Path' does not exist." }

    return @(Get-Content -LiteralPath $Path | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') })
}

function Test-Line {
    <# Returns the ids of the rules a line of text breaks. The text itself is never returned. #>
    param([string]$Line, [string[]]$Names)

    $hits = [System.Collections.Generic.List[string]]::new()

    foreach ($rule in $SecretRules) {
        if ($Line -match $rule.Pattern) { $hits.Add($rule.Id) }
    }

    foreach ($match in [regex]::Matches($Line, $EmailPattern)) {
        $domain = $match.Value.Substring($match.Value.IndexOf('@') + 1)
        if ($domain -notmatch $AllowedEmailDomain) { $hits.Add('email-address'); break }
    }

    if ($Line -match $PhonePattern) { $hits.Add('telephone-number') }

    for ($i = 0; $i -lt $Names.Count; $i++) {
        if ($Line.IndexOf($Names[$i], [StringComparison]::OrdinalIgnoreCase) -ge 0) { $hits.Add("private-name-$($i + 1)") }
    }

    return $hits
}

function Invoke-Git {
    param([string]$Root, [string[]]$Arguments)

    $output = & git -C $Root @Arguments 2>&1
    if ($LASTEXITCODE -ne 0) { throw "git $($Arguments -join ' ') failed: $output" }

    return $output
}

function Invoke-Sweep {
    <# Sweeps one repository and returns the findings as 'rule | where' strings. #>
    param([string]$Root, [string[]]$Names)

    $findings = [System.Collections.Generic.SortedSet[string]]::new([StringComparer]::Ordinal)

    # Who made every commit: only no-reply addresses.
    $people = Invoke-Git $Root @('log', '--all', '--format=%an <%ae>%n%cn <%ce>') | Where-Object { $_ } | Sort-Object -Unique
    foreach ($person in $people) {
        if ($person -notmatch $AllowedIdentity) { [void]$findings.Add('author-or-committer-not-a-no-reply-address | history') }
    }

    # Commit messages.
    $messages = (Invoke-Git $Root @('log', '--all', '--format=commit %h%n%B%n')) -join "`n"
    $current = '?'
    foreach ($line in $messages -split "`n") {
        if ($line -match '^commit ([0-9a-f]{4,})$') { $current = $Matches[1]; continue }
        foreach ($id in Test-Line $line $Names) { [void]$findings.Add("$id | message of commit $current") }
    }

    # Every line added by every commit on every branch, including what was removed again later.
    $commit = '?'
    $file = '?'
    foreach ($line in Invoke-Git $Root @('log', '--all', '-p', '-U0', '--no-color', '--no-ext-diff', '--format=commit %h')) {
        if ($line -match '^commit ([0-9a-f]{4,})$') { $commit = $Matches[1]; continue }
        if ($line -match '^diff --git a/.* b/(.*)$') {
            $file = $Matches[1]
            if ($file -match $ForbiddenFile) { [void]$findings.Add("forbidden-file | $file (commit $commit)") }
            continue
        }
        if (-not $line.StartsWith('+') -or $line.StartsWith('+++')) { continue }
        if ($file -match $BinaryExtension) { continue }
        foreach ($id in Test-Line $line.Substring(1) $Names) { [void]$findings.Add("$id | $file (commit $commit)") }
    }

    # Every file in the tree that is tracked or not ignored, line by line.
    foreach ($path in Invoke-Git $Root @('ls-files', '-co', '--exclude-standard')) {
        if ($path -match $ForbiddenFile) { [void]$findings.Add("forbidden-file | $path (working tree)"); continue }
        if ($path -match $BinaryExtension) { continue }

        $full = Join-Path $Root $path
        if (-not (Test-Path -LiteralPath $full -PathType Leaf)) { continue }

        $number = 0
        foreach ($line in [System.IO.File]::ReadLines($full)) {
            $number++
            foreach ($id in Test-Line $line $Names) { [void]$findings.Add("$id | ${path}:$number") }
        }
    }

    return @($findings)
}

# ---- The self-test ----

function Invoke-SelfTest {
    $root = Join-Path ([IO.Path]::GetTempPath()) ("publication-sweep-selftest-" + [Guid]::NewGuid().ToString('N'))
    $names = @('Zorblax Industries')

    # Every planted value is built from parts at run time, so this file never contains a working shape itself.
    $planted = [ordered]@{
        'cloud-access-key'              = 'key = "AKIA' + 'ABCDEFGHIJKLMNOP"'
        'github-token'                  = 'token = ghp' + '_' + ('a1B2c3D4' * 5)
        'payment-key'                   = 'sk' + '_live_' + 'abcdefghijklmnop1234'
        'private-key-block'             = '-----BEGIN RSA ' + 'PRIVATE KEY-----'
        'chat-token'                    = 'xox' + 'b-123456789012-abcdefghijkl'
        'google-api-key'                = 'AIza' + ('Sy' * 17) + 'ab1'
        'json-web-token'                = 'eyJ' + 'hbGciOiJIUzI1NiJ9.eyJ' + 'zdWIiOiIxMjM0NTY3ODkwIn0.abcdefghijklmnop'
        'bearer-token-in-header'        = 'Authorization: Bearer ' + ('abcdEFGH1234' * 3)
        'password-in-connection-string' = 'Server=db;User Id=app;Pass' + 'word=hunter2hunter2;'
        'email-address'                 = 'contact: someone' + '@mail-provider.example.net.example.com.invalid.co'
        'telephone-number'              = 'call 316' + '-555-0142 for help'
        'private-name-1'                = 'built for Zorblax' + ' Industries'
    }

    try {
        New-Item -ItemType Directory -Path $root | Out-Null
        Invoke-Git $root @('init', '-q', '-b', 'main') | Out-Null
        Invoke-Git $root @('config', 'user.name', 'Test Person') | Out-Null
        Invoke-Git $root @('config', 'user.email', '1+person@users.noreply.github.com') | Out-Null
        Invoke-Git $root @('config', 'commit.gpgsign', 'false') | Out-Null

        # A clean repository first: the sweep must find nothing.
        Set-Content -LiteralPath (Join-Path $root 'readme.txt') -Value @('A fictional town.', 'Reach us at hello@bramblewick.test', 'Built by nobody in particular.', '<div class="category-@group.Key">', 'npm install @types/node')
        Invoke-Git $root @('add', '-A') | Out-Null
        Invoke-Git $root @('commit', '-q', '-m', 'A clean start') | Out-Null
        $clean = @(Invoke-Sweep $root $names)
        if ($clean.Count -ne 0) { throw "The self-test failed: a clean repository was reported as having $($clean.Count) finding(s): $($clean -join '; ')" }

        # One planted defect per rule, in its own file; a certificate-like file; a secret added and removed again.
        $i = 0
        foreach ($entry in $planted.GetEnumerator()) {
            $i++
            Set-Content -LiteralPath (Join-Path $root "planted-$i.txt") -Value $entry.Value
        }
        Set-Content -LiteralPath (Join-Path $root 'server.pfx') -Value 'not really a certificate'
        Set-Content -LiteralPath (Join-Path $root 'oops.txt') -Value ('old = AKIA' + 'ZYXWVUTSRQPONMLK')
        Invoke-Git $root @('add', '-A') | Out-Null
        Invoke-Git $root @('commit', '-q', '-m', 'Planted defects') | Out-Null
        Invoke-Git $root @('rm', '-q', 'oops.txt') | Out-Null
        Invoke-Git $root @('commit', '-q', '-m', 'Remove the secret again, which leaves it in history') | Out-Null

        # Each of the next defects is placed where only ONE part of the sweep can see it, so removing that part is noticed.
        # 1. A credential in a commit message and nowhere else (only the message pass reads it).
        Set-Content -LiteralPath (Join-Path $root 'extra.txt') -Value 'nothing special'
        Invoke-Git $root @('add', '-A') | Out-Null
        Invoke-Git $root @('commit', '-q', '-m', ('Rotate the key ' + 'sk' + '_live_' + 'zyxwvutsrqponm9876')) | Out-Null

        # 2. A credential and a personal author address that exist only on a second branch that is not checked out
        #    (only the passes that read every branch, not just the current one, see them).
        Invoke-Git $root @('checkout', '-q', '-b', 'side') | Out-Null
        Set-Content -LiteralPath (Join-Path $root 'side-only.txt') -Value ('token = ghp' + '_' + ('Z9y8X7w6' * 5))
        Invoke-Git $root @('add', '-A') | Out-Null
        Invoke-Git $root @('-c', 'user.email=person@mail-provider.example.net', 'commit', '-q', '-m', 'On the other branch') | Out-Null
        Invoke-Git $root @('checkout', '-q', 'main') | Out-Null

        # 3. A file that was never added, with a defect in it, and a key file that was never added (only the tree pass,
        #    reading untracked files, sees them).
        Set-Content -LiteralPath (Join-Path $root 'untracked-note.txt') -Value ('call 316' + '-555-0199 tonight')
        Set-Content -LiteralPath (Join-Path $root 'untracked.pem') -Value 'not really a key'

        # 4. A defect added to a tracked file and not committed (only the tree pass sees it, and only at its line).
        Add-Content -LiteralPath (Join-Path $root 'readme.txt') -Value ('old = AKIA' + 'QWERTYUIOPASDFGH')

        $found = @(Invoke-Sweep $root $names)
        $missing = @()
        foreach ($id in $planted.Keys) {
            if (-not ($found | Where-Object { $_.StartsWith("$id |") })) { $missing += $id }
        }

        # Each expectation names the rule AND where it must be reported.
        $expectations = [ordered]@{
            'a secret removed again but still in history'          = 'cloud-access-key | oops.txt (commit '
            'a forbidden file in the history'                      = 'forbidden-file | server.pfx (commit '
            'a forbidden file in the working tree'                 = 'forbidden-file | server.pfx (working tree)'
            'a credential in a commit message only'                = 'payment-key | message of commit '
            'a credential only on another branch'                  = 'github-token | side-only.txt (commit '
            'a personal author address only on another branch'     = 'author-or-committer-not-a-no-reply-address | history'
            'a defect in an untracked file'                        = 'telephone-number | untracked-note.txt:1'
            'an untracked key file'                                = 'forbidden-file | untracked.pem (working tree)'
            'an uncommitted change to a tracked file'              = 'cloud-access-key | readme.txt:6'
        }

        foreach ($pair in $expectations.GetEnumerator()) {
            if (-not ($found | Where-Object { $_.StartsWith($pair.Value) })) { $missing += $pair.Key }
        }

        if ($missing.Count -gt 0) { throw "The self-test failed: the sweep did not report: $($missing -join '; ')" }

        Write-Host "Self-test passed: $($planted.Count) planted defects, each of the nine places only one part of the sweep can see (history, a message, another branch, the tree, an untracked file, an uncommitted change) and a personal author address were all found, and a clean repository found nothing."
    }    finally {
        if (Test-Path -LiteralPath $root) {
            # Git marks its object files read-only on some systems.
            Get-ChildItem -LiteralPath $root -Recurse -Force | ForEach-Object { try { $_.Attributes = 'Normal' } catch { $null = $_ } }
            Remove-Item -LiteralPath $root -Recurse -Force -ErrorAction SilentlyContinue
        }
    }
}

# ---- Main ----

if ($SelfTest) {
    Invoke-SelfTest
    exit 0
}

$repository = (Resolve-Path (Join-Path $PSScriptRoot '..')).Path
$privateNames = @(Get-Names $NamesFile)

if ($privateNames.Count -eq 0) {
    Write-Host 'Private names: SKIPPED (no -NamesFile or CP_SWEEP_NAMES_FILE given). The other checks still ran.'
}
else {
    Write-Host "Private names: checking $($privateNames.Count) name(s) from the names file."
}

$results = @(Invoke-Sweep $repository $privateNames)
$accepted = if ([string]::IsNullOrWhiteSpace($AcceptedFile)) { @() } else { @(Get-Content -LiteralPath $AcceptedFile | ForEach-Object { $_.Trim() } | Where-Object { $_ -and -not $_.StartsWith('#') }) }
$knownCount = @($results | Where-Object { $accepted -contains $_ }).Count
$results = @($results | Where-Object { $accepted -notcontains $_ })

if ($knownCount -gt 0) {
    Write-Host "Accepted findings: $knownCount (listed in the accepted file; they are in history that cannot be rewritten)."
}

if ($results.Count -eq 0) {
    Write-Host 'Publication sweep: nothing new found in the history or the working tree.'
    exit 0
}

Write-Host "Publication sweep: $($results.Count) finding(s). The rule and where are shown; what matched is not."
$results | ForEach-Object { Write-Host "  $_" }
exit 1
