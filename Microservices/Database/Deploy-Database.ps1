<#
.SYNOPSIS
    Creates or updates the Orvanta database from the scripts in this folder.

.DESCRIPTION
    Database/Orvanta holds every object of the single Orvanta database (all in the default schema, so
    tables are simply Users, AppLogs, ...):
        PreDeployment/     run first (one-time structural moves, idempotent)
        Tables/            <Table>.sql; ordered automatically so a table comes after every table it REFERENCES
        Functions/         <ufn_Name>.sql; procedures may use them
        StoredProcedures/  <usp_Name>.sql; run last
    Each file is named after the object it creates; no numbers or prefixes are needed.
    Every script is idempotent (IF ... IS NULL / CREATE OR ALTER), so this can run on every start-up and in
    every release. A failing script stops the deployment with the file name and the SQL error.

    -IncludeMigrations also runs Database/Migrations/*.sql (one-time data moves, also idempotent).

.EXAMPLE
    powershell -ExecutionPolicy Bypass -File .\Microservices\Database\Deploy-Database.ps1
.EXAMPLE
    .\Deploy-Database.ps1 -Server "myserver" -UserName sa -Password "..." -IncludeMigrations
#>
[CmdletBinding()]
param(
    [string]$Server = 'localhost\SQLEXPRESS',
    [string]$Database = 'Orvanta',
    # SQL login. Omit both to use Windows authentication.
    [string]$UserName,
    [string]$Password,
    [switch]$IncludeMigrations
)

$ErrorActionPreference = 'Stop'
$root = Split-Path -Parent $MyInvocation.MyCommand.Path
$source = Join-Path $root 'Orvanta'
$stages = @('PreDeployment', 'Tables', 'Functions', 'StoredProcedures')

if (-not (Get-Command sqlcmd -ErrorAction SilentlyContinue)) {
    throw 'sqlcmd was not found. Install it with: winget install Microsoft.Sqlcmd'
}

function Invoke-Sql {
    param([string]$Db, [string]$File, [string]$Query)

    # -b abort on error, -I QUOTED_IDENTIFIER ON (filtered indexes need it), -C trust the local dev certificate.
    $arguments = @('-S', $Server, '-d', $Db, '-b', '-I', '-C')
    if ($UserName) { $arguments += @('-U', $UserName, '-P', $Password) } else { $arguments += '-E' }
    if ($File) { $arguments += @('-i', $File) } else { $arguments += @('-Q', $Query) }

    $output = & sqlcmd @arguments 2>&1
    if ($LASTEXITCODE -ne 0) {
        $target = if ($File) { $File } else { $Query }
        throw "Failed on [$Db] $target`n$($output -join "`n")"
    }
    $output | Where-Object { $_ -and "$_".Trim() } | ForEach-Object { Write-Host "      $_" }
}

# Orders table scripts so each comes after the tables its foreign keys reference (file name = table name).
function Get-TableOrder {
    param([System.IO.FileInfo[]]$Files)

    $byName = @{}
    foreach ($file in $Files) { $byName[$file.BaseName] = $file }

    $dependsOn = @{}
    foreach ($file in $Files) {
        $sql = Get-Content $file.FullName -Raw
        $refs = [regex]::Matches($sql, 'REFERENCES\s+(?:\[?dbo\]?\.)?\[?(\w+)\]?', 'IgnoreCase') |
            ForEach-Object { $_.Groups[1].Value } |
            Where-Object { $_ -ne $file.BaseName -and $byName.ContainsKey($_) } |
            Select-Object -Unique
        $dependsOn[$file.BaseName] = @($refs)
    }

    $ordered = New-Object System.Collections.Generic.List[System.IO.FileInfo]
    $state = @{}   # 1 = visiting, 2 = done
    function Visit([string]$name) {
        if ($state[$name] -eq 2) { return }
        if ($state[$name] -eq 1) { throw "Circular foreign keys involving table '$name'." }
        $state[$name] = 1
        foreach ($dep in $dependsOn[$name]) { Visit $dep }
        $state[$name] = 2
        $ordered.Add($byName[$name])
    }
    foreach ($name in ($byName.Keys | Sort-Object)) { Visit $name }
    return $ordered
}

Write-Host "== $Database on $Server" -ForegroundColor Cyan

# SQL Express creates databases with AUTO_CLOSE ON, which adds multi-second delays after idle periods.
# Azure SQL (EngineEdition 5) databases are created by infra/main.bicep and don't support AUTO_CLOSE, so skip both there.
Invoke-Sql -Db 'master' -Query "IF CAST(SERVERPROPERTY('EngineEdition') AS INT) <> 5 BEGIN IF DB_ID(N'$Database') IS NULL CREATE DATABASE [$Database]; EXEC(N'ALTER DATABASE [$Database] SET AUTO_CLOSE OFF'); END"

foreach ($stage in $stages) {
    $dir = Join-Path $source $stage
    if (-not (Test-Path $dir)) { continue }
    $scripts = Get-ChildItem $dir -Filter *.sql | Sort-Object Name
    if ($stage -eq 'Tables') { $scripts = Get-TableOrder $scripts }
    foreach ($script in $scripts) {
        Invoke-Sql -Db $Database -File $script.FullName
        Write-Host "   $stage/$($script.Name)"
    }
}

if ($IncludeMigrations) {
    foreach ($script in Get-ChildItem (Join-Path $root 'Migrations') -Filter *.sql -ErrorAction SilentlyContinue | Sort-Object Name) {
        Write-Host "   Migrations/$($script.Name)"
        Invoke-Sql -Db $Database -File $script.FullName
    }
}

Write-Host 'Database deployment complete.' -ForegroundColor Green
