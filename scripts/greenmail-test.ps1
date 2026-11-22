# ThixMail against a real IMAP/SMTP server: a THROWAWAY GreenMail container (own compose project, random high ports on 127.0.0.1, tmpfs, nothing
# kept), then the env-gated RealServerTests of tests/ThixMail.Core.Tests, then everything is removed again (containers, network, volumes; the image stays in Docker's cache like any pulled image).
# Usage (Windows PowerShell or pwsh, Docker running):  ./scripts/greenmail-test.ps1
#        ./scripts/greenmail-test.ps1 -Large 20000   also runs LargeMailboxTests: that many messages in INBOX + 60 folders of 50, the numbers into
#                                                    large-mailbox-report.txt next to this script's temporary folder (printed at the end)
param([int]$Large = 0)
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = "thixmail-test-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
$folder = Join-Path ([IO.Path]::GetTempPath()) $project
New-Item -ItemType Directory -Force $folder | Out-Null
$compose = Join-Path $folder "compose.yml"
$preload = Join-Path $folder "preload"
$preloadOption = ""
$preloadVolume = ""
if ($Large -gt 0) {
    # the big folder as files GreenMail reads at its start (appending them one by one takes it far too long)
    New-Item -ItemType Directory -Force $preload | Out-Null
    $env:THIXMAIL_LARGE_COUNT = "$Large"
    $env:THIXMAIL_LARGE_GENERATE = $preload
    $env:THIXMAIL_USER = "anna@thix.test"
    dotnet test (Join-Path $root "tests/ThixMail.Core.Tests") --filter "FullyQualifiedName~Generate_the_preload" -nologo | Out-Null
    Remove-Item Env:THIXMAIL_LARGE_GENERATE
    $preloadOption = " -Dgreenmail.preload.dir=/preload"
    $preloadVolume = "    volumes:`n      - ""$($preload.Replace('\', '/')):/preload:ro""`n"
}
@"
services:
  greenmail:
    image: greenmail/standalone:2.1.14
    environment:
      JAVA_OPTS: "-Xmx1g"
      GREENMAIL_OPTS: "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.auth.disabled=false -Dgreenmail.users=anna:geheim@thix.test -Dgreenmail.users.login=email$preloadOption"
    ports:
      - "127.0.0.1::3025"
      - "127.0.0.1::3143"
      - "127.0.0.1::3993"
    tmpfs:
      - /tmp
$preloadVolume
"@ | Set-Content -Encoding utf8 $compose

function Get-HostPort([string]$containerPort) {
    $line = docker compose -p $project -f $compose port greenmail $containerPort
    return ($line -split ":")[-1].Trim()
}

try {
    docker compose -p $project -f $compose up -d | Out-Null
    $imap = Get-HostPort 3143
    $imaps = Get-HostPort 3993
    $smtp = Get-HostPort 3025
    # wait until IMAP answers
    for ($i = 0; $i -lt 60; $i++) {
        try { $client = New-Object Net.Sockets.TcpClient("127.0.0.1", [int]$imap); $client.Close(); break } catch { Start-Sleep -Milliseconds 500 }
    }
    Start-Sleep -Seconds 2
    $env:THIXMAIL_IMAP_HOST = "127.0.0.1"
    $env:THIXMAIL_IMAP_PORT = $imap
    $env:THIXMAIL_IMAPS_PORT = $imaps
    $env:THIXMAIL_SMTP_PORT = $smtp
    $env:THIXMAIL_USER = "anna@thix.test"
    $env:THIXMAIL_PASSWORD = "geheim"
    $code = 0
    if ($Large -gt 0) {
        # the big mailbox only (the test waits until GreenMail has read the preload); the ordinary run is the script without -Large
        $report = Join-Path ([IO.Path]::GetTempPath()) "thixmail-large-mailbox-report.txt"
        Remove-Item $report -ErrorAction SilentlyContinue
        $env:THIXMAIL_LARGE_COUNT = "$Large"
        $env:THIXMAIL_LARGE_REPORT = $report
        $env:THIXMAIL_LARGE_PRELOADED = "1"
        dotnet test (Join-Path $root "tests/ThixMail.Core.Tests") --filter "FullyQualifiedName~LargeMailboxTests" -nologo
        $code = $LASTEXITCODE
        if (Test-Path $report) { Write-Host ""; Write-Host "Large mailbox ($report):"; Get-Content -Encoding UTF8 $report | Write-Host }
    }

    if ($Large -eq 0) {
        # (not on top of the big mailbox: GreenMail needs more than MailKit's 60 s for some commands on a folder of 20,000 – a real server does not)
        dotnet test (Join-Path $root "tests/ThixMail.Core.Tests") --filter "FullyQualifiedName~RealServerTests" -nologo
        $code = $LASTEXITCODE
    }
}
finally {
    if ($code -ne 0) {
        # what the server said, for the failure (kept next to the report)
        $serverLog = Join-Path ([IO.Path]::GetTempPath()) "thixmail-greenmail-server.log"
        docker compose -p $project -f $compose logs --no-color --tail 400 greenmail *> $serverLog
        Write-Host "GreenMail's log: $serverLog"
    }

    foreach ($name in "THIXMAIL_IMAP_HOST", "THIXMAIL_IMAP_PORT", "THIXMAIL_IMAPS_PORT", "THIXMAIL_SMTP_PORT", "THIXMAIL_USER", "THIXMAIL_PASSWORD", "THIXMAIL_LARGE_COUNT", "THIXMAIL_LARGE_REPORT", "THIXMAIL_LARGE_PRELOADED") {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
    docker compose -p $project -f $compose down -v --remove-orphans | Out-Null
    Remove-Item -Recurse -Force $folder -ErrorAction SilentlyContinue
}
exit $code
