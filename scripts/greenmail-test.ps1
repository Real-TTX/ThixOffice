# ThixMail against a real IMAP/SMTP server: a THROWAWAY GreenMail container (own compose project, random high ports on 127.0.0.1, tmpfs, nothing
# kept), then the env-gated RealServerTests of tests/ThixMail.Core.Tests, then everything is removed again (containers, network, volumes; the image stays in Docker's cache like any pulled image).
# Usage (Windows PowerShell or pwsh, Docker running):  ./scripts/greenmail-test.ps1
$ErrorActionPreference = "Stop"
$root = Split-Path -Parent $PSScriptRoot
$project = "thixmail-test-" + [Guid]::NewGuid().ToString("N").Substring(0, 8)
$folder = Join-Path ([IO.Path]::GetTempPath()) $project
New-Item -ItemType Directory -Force $folder | Out-Null
$compose = Join-Path $folder "compose.yml"
@"
services:
  greenmail:
    image: greenmail/standalone:2.1.14
    environment:
      GREENMAIL_OPTS: "-Dgreenmail.setup.test.all -Dgreenmail.hostname=0.0.0.0 -Dgreenmail.auth.disabled=false -Dgreenmail.users=anna:geheim@thix.test -Dgreenmail.users.login=email"
    ports:
      - "127.0.0.1::3025"
      - "127.0.0.1::3143"
      - "127.0.0.1::3993"
    tmpfs:
      - /tmp
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
    dotnet test (Join-Path $root "tests/ThixMail.Core.Tests") --filter "FullyQualifiedName~RealServerTests" -nologo
    $code = $LASTEXITCODE
}
finally {
    foreach ($name in "THIXMAIL_IMAP_HOST", "THIXMAIL_IMAP_PORT", "THIXMAIL_IMAPS_PORT", "THIXMAIL_SMTP_PORT", "THIXMAIL_USER", "THIXMAIL_PASSWORD") {
        Remove-Item "Env:$name" -ErrorAction SilentlyContinue
    }
    docker compose -p $project -f $compose down -v | Out-Null
    Remove-Item -Recurse -Force $folder -ErrorAction SilentlyContinue
}
exit $code
