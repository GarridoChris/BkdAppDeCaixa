<#
.SYNOPSIS
Creates local PostgreSQL backups before the salon data import.

.DESCRIPTION
Backs up the schema and data from both postgres and organizaeh. Each archive
is decoded into the Windows null device to check that it is readable.
The script does not restore or modify either database.

.EXAMPLE
.\backend\scripts\Backup-SalonDatabases.ps1 -DbUser postgres
#>

[CmdletBinding()]
param(
    [string] $Server = 'localhost',
    [ValidateRange(1, 65535)]
    [int] $Port = 5432,
    [string] $DbUser = 'postgres',
    [string] $OutputDirectory = (Join-Path $env:LOCALAPPDATA 'AppDeCaixa\PostgresBackups'),
    [string] $PostgresBinDirectory = (Join-Path $env:ProgramFiles 'PostgreSQL\16\bin')
)

$ErrorActionPreference = 'Stop'

$pgDump = Join-Path $PostgresBinDirectory 'pg_dump.exe'
$pgRestore = Join-Path $PostgresBinDirectory 'pg_restore.exe'
foreach ($program in @($pgDump, $pgRestore)) {
    if (-not (Test-Path -LiteralPath $program -PathType Leaf)) {
        throw "PostgreSQL tool not found at '$program'. Set -PostgresBinDirectory to your PostgreSQL bin folder."
    }
}

$backupId = '{0}-{1}' -f (Get-Date -Format 'yyyyMMdd-HHmmss'), ([guid]::NewGuid().ToString('N').Substring(0, 8))
$backupRoot = New-Item -ItemType Directory -Path $OutputDirectory -Force
$backupDirectory = Join-Path $backupRoot.FullName $backupId
New-Item -ItemType Directory -Path $backupDirectory | Out-Null

$connectionArgs = @('--host', $Server, '--port', $Port, '--username', $DbUser)

foreach ($databaseName in @('postgres', 'organizaeh')) {
    $archivePath = Join-Path $backupDirectory "$databaseName.dump"
    $partialPath = "$archivePath.partial"
    Write-Host "Backing up $databaseName. PostgreSQL may prompt for your password."

    try {
        & $pgDump @connectionArgs --format=custom --file $partialPath --dbname $databaseName
        if ($LASTEXITCODE -ne 0) {
            throw "pg_dump failed for '$databaseName' with exit code $LASTEXITCODE."
        }

        & $pgRestore --file=NUL $partialPath
        if ($LASTEXITCODE -ne 0) {
            throw "pg_restore could not read the '$databaseName' backup. Exit code: $LASTEXITCODE."
        }

        Move-Item -LiteralPath $partialPath -Destination $archivePath
        Write-Host "Backup ready: $archivePath"
    }
    finally {
        if (Test-Path -LiteralPath $partialPath -PathType Leaf) {
            Remove-Item -LiteralPath $partialPath
        }
    }
}

Write-Host "Both database backups are ready in $backupDirectory"
