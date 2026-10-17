# Test-10eLottoArchive.ps1
# Sequentially checks archived 10eLotto pages for different dates.
# Compatible with Windows PowerShell 5.1 and PowerShell 7+.
#
# Examples:
#   .\Test-10eLottoArchive.ps1
#   .\Test-10eLottoArchive.ps1 -Count 25
#   .\Test-10eLottoArchive.ps1 -Count 100 -StartDate '2026-10-01' -DelayMilliseconds 2000
#   .\Test-10eLottoArchive.ps1 -Count 10 -ReportPath 'C:\Temp\10elotto-results.csv'
#
# The first request is for StartDate; subsequent requests go backwards one day.
# Requests are sequential, with a delay. HTTP 403/429 or a detectable challenge
# stops the test rather than attempting to bypass access restrictions.

[CmdletBinding()]
param(
    [ValidateRange(1, 10000)]
    [int]$Count = 100,

    [datetime]$StartDate = [datetime]'2026-10-01',

    [ValidateRange(0, 60000)]
    [int]$DelayMilliseconds = 1000,

    [ValidateRange(5, 300)]
    [int]$TimeoutSeconds = 30,

    [string]$ReportPath = ''
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

if ([string]::IsNullOrWhiteSpace($ReportPath)) {
    $ReportPath = Join-Path $PSScriptRoot '10elotto-archive-test-results.csv'
}

$reportFolder = Split-Path -Path $ReportPath -Parent
if ($reportFolder -and -not (Test-Path -LiteralPath $reportFolder)) {
    New-Item -ItemType Directory -Path $reportFolder -Force | Out-Null
}

function Test-ArchiveHtml {
    param(
        [Parameter(Mandatory = $true)][string]$Html,
        [Parameter(Mandatory = $true)][string]$ExpectedDate
    )

    $headingFound = $false
    $dateMatched = $false
    $headingText = ''

    foreach ($match in [regex]::Matches($Html, '(?is)<h2\b[^>]*>(.*?)</h2\s*>')) {
        $plain = [regex]::Replace($match.Groups[1].Value, '(?s)<[^>]*>', ' ')
        $plain = [System.Net.WebUtility]::HtmlDecode($plain)
        $plain = [regex]::Replace($plain, '\s+', ' ').Trim()

        if ($plain -match 'Estrazioni\s+10eLotto\s+5\s+minuti') {
            $headingFound = $true
            $headingText = $plain
            if ($plain.Contains($ExpectedDate)) {
                $dateMatched = $true
                break
            }
        }
    }

    $tableFound = $false
    $trCount = 0

    foreach ($tableTag in [regex]::Matches($Html, '(?is)<table\b[^>]*>')) {
        $classAttr = [regex]::Match(
            $tableTag.Value,
            '\bclass\s*=\s*(?:"([^"]*)"|''([^'']*)''|([^\s>]+))',
            [System.Text.RegularExpressions.RegexOptions]::IgnoreCase
        )
        if (-not $classAttr.Success) { continue }

        $classes = ($classAttr.Groups[1].Value + $classAttr.Groups[2].Value + $classAttr.Groups[3].Value) -split '\s+'
        if ($classes -notcontains 'tabella-desktop') { continue }

        $tableFound = $true

        # Locate the matching closing </table>, even if nested tables exist.
        $remaining = $Html.Substring($tableTag.Index)
        $depth = 0
        $endIndex = $remaining.Length
        foreach ($token in [regex]::Matches($remaining, '(?is)<\s*(/?)\s*table\b[^>]*>')) {
            if ($token.Groups[1].Value -eq '/') { $depth-- } else { $depth++ }
            if ($depth -eq 0) {
                $endIndex = $token.Index
                break
            }
        }

        $tableContent = $remaining.Substring(0, $endIndex)
        $trCount = [regex]::Matches($tableContent, '(?i)<tr\b').Count
        break
    }

    return [pscustomobject]@{
        TableFound         = $tableFound
        TableTrCount       = $trCount
        HeadingFound       = $headingFound
        HeadingDateMatched = $dateMatched
        HeadingText        = $headingText
    }
}

$results = @()
$blocked = $false

Write-Host "Testing $Count distinct dates, starting at $($StartDate.ToString('yyyy-MM-dd')) and moving backwards."
Write-Host "Delay: $DelayMilliseconds ms | Timeout: $TimeoutSeconds sec | CSV: $ReportPath"
Write-Host ''

for ($i = 0; $i -lt $Count; $i++) {
    $date = $StartDate.Date.AddDays(-$i)
    $queryDate = $date.ToString('yyyy-MM-dd', [Globalization.CultureInfo]::InvariantCulture)
    $expectedHeadingDate = $date.ToString('dd/MM/yyyy', [Globalization.CultureInfo]::InvariantCulture)
    $uri = "https://www.10elotto5.it/archivio-10elotto5-minuti/?data=$queryDate"

    $httpStatus = 0
    $html = ''
    $errorMessage = ''
    $validation = $null
    $watch = [System.Diagnostics.Stopwatch]::StartNew()

    try {
        $response = Invoke-WebRequest -Uri $uri -Method Get -UseBasicParsing `
            -TimeoutSec $TimeoutSeconds -ErrorAction Stop
        $httpStatus = [int]$response.StatusCode
        $html = [string]$response.Content

        if ($httpStatus -eq 200) {
            $validation = Test-ArchiveHtml -Html $html -ExpectedDate $expectedHeadingDate
        }
    }
    catch {
        $errorMessage = $_.Exception.Message
        # Windows PowerShell throws on non-2xx HTTP responses. Capture the status.
        $responseProperty = $_.Exception.PSObject.Properties['Response']
        if ($null -ne $responseProperty -and $null -ne $responseProperty.Value) {
            try { $httpStatus = [int]$responseProperty.Value.StatusCode } catch { }
        }
    }
    finally {
        $watch.Stop()
    }

    if ($null -eq $validation) {
        $validation = [pscustomobject]@{
            TableFound = $false
            TableTrCount = 0
            HeadingFound = $false
            HeadingDateMatched = $false
            HeadingText = ''
        }
    }

    $passed = (
        $httpStatus -eq 200 -and
        $validation.TableFound -and
        $validation.TableTrCount -gt 100 -and
        $validation.HeadingFound -and
        $validation.HeadingDateMatched
    )

    # A challenge may respond with HTTP 200 but show a CAPTCHA instead of the data.
    $challengeDetected = (-not $passed) -and (
        $html -match '(?i)cf-chl|captcha|checking your browser|verify (that )?you are human|rate limit exceeded|access denied'
    )
    $isBlocked = ($httpStatus -eq 403 -or $httpStatus -eq 429 -or $challengeDetected)

    $row = [pscustomobject][ordered]@{
        Date               = $queryDate
        HttpStatus         = $httpStatus
        TableFound         = $validation.TableFound
        TableTrCount       = $validation.TableTrCount
        HeadingFound       = $validation.HeadingFound
        HeadingDateMatched = $validation.HeadingDateMatched
        ExpectedDateInH2   = $expectedHeadingDate
        HeadingText        = $validation.HeadingText
        Passed             = $passed
        BlockDetected      = $isBlocked
        ElapsedMs          = $watch.ElapsedMilliseconds
        Error              = $errorMessage
        Url                = $uri
    }

    $results += $row
    if ($results.Count -eq 1) {
        $row | Export-Csv -LiteralPath $ReportPath -NoTypeInformation -Encoding UTF8
    }
    else {
        $row | Export-Csv -LiteralPath $ReportPath -Append -NoTypeInformation -Encoding UTF8
    }

    $statusLabel = if ($passed) { 'PASS' } else { 'FAIL' }
    Write-Host ("{0,3}/{1}  {2}  HTTP={3}  TR={4}  H2={5}  Date={6}  {7}" -f `
        ($i + 1), $Count, $queryDate, $httpStatus, $validation.TableTrCount, `
        $validation.HeadingFound, $validation.HeadingDateMatched, $statusLabel)

    if ($errorMessage) { Write-Warning $errorMessage }
    if ($isBlocked) {
        Write-Warning "Possible server-side block or challenge on $queryDate. Stopping without sending more requests."
        $blocked = $true
        break
    }

    if ($i -lt ($Count - 1) -and $DelayMilliseconds -gt 0) {
        Start-Sleep -Milliseconds $DelayMilliseconds
    }
}

$passedCount = @($results | Where-Object { $_.Passed }).Count
$failedCount = $results.Count - $passedCount
Write-Host ''
Write-Host "Finished: $($results.Count)/$Count requests | Passed: $passedCount | Failed: $failedCount"
Write-Host "CSV report: $ReportPath"

if ($blocked) { exit 2 }
if ($failedCount -gt 0) { exit 1 }
exit 0
