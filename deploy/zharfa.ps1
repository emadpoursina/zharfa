# Start, stop and inspect the Zharfa Demo 1 stack (Windows PowerShell / PowerShell 7).
#
#   .\deploy\zharfa.ps1 up        build (if needed) and start in the background
#   .\deploy\zharfa.ps1 down      stop; database data is kept
#   .\deploy\zharfa.ps1 restart   rebuild and restart everything
#   .\deploy\zharfa.ps1 status    show container status
#   .\deploy\zharfa.ps1 logs      follow API logs (Ctrl+C to stop)
#   .\deploy\zharfa.ps1 reset     stop AND delete the database (asks first)
#
# If PowerShell blocks the script, run it once with:
#   powershell -ExecutionPolicy Bypass -File .\deploy\zharfa.ps1 up

param(
    [ValidateSet('up', 'down', 'restart', 'status', 'logs', 'reset')]
    [string]$Command = 'up'
)

$ErrorActionPreference = 'Stop'
Set-Location (Join-Path $PSScriptRoot '..')

$files = @('-f', 'deploy/docker-compose.yml')
if (Test-Path 'deploy/docker-compose.9router.yml') {
    $files += @('-f', 'deploy/docker-compose.9router.yml')
}

function Invoke-Compose {
    & docker compose @files @args
    if ($LASTEXITCODE -ne 0) {
        throw "docker compose exited with code $LASTEXITCODE"
    }
}

function Assert-EnvFile {
    if (-not (Test-Path 'deploy/.env')) {
        Write-Error "deploy/.env is missing. Copy deploy/.env.example to deploy/.env and set POSTGRES_PASSWORD."
    }
}

switch ($Command) {
    'up' {
        Assert-EnvFile
        Invoke-Compose up --build -d
        Write-Host ''
        Write-Host 'Zharfa is starting. Open http://localhost:8080 (or the ZHARFA_PORT in deploy/.env).'
    }
    'restart' {
        Assert-EnvFile
        Invoke-Compose up --build -d --force-recreate
    }
    'down' {
        Invoke-Compose down
    }
    'status' {
        Invoke-Compose ps
    }
    'logs' {
        Invoke-Compose logs -f api
    }
    'reset' {
        $answer = Read-Host "This deletes the Zharfa database volume. Type 'yes' to continue"
        if ($answer -eq 'yes') {
            Invoke-Compose down -v
            Write-Host "Database removed. Run '.\deploy\zharfa.ps1 up' to start fresh."
        } else {
            Write-Host 'Cancelled.'
        }
    }
}
