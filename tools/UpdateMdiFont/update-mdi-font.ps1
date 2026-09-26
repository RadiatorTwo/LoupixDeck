#requires -Version 7
<#
.SYNOPSIS
    Checks and updates the bundled Material Design icon fonts and their symbol catalogs.

.DESCRIPTION
    Each bundled icon font (Material Design Icons, Material Design Light) has a catalog next to it in
    LoupixDeck/Assets/Fonts (mdi-catalog.json, mdil-catalog.json). The catalog records the npm package
    version and the SHA-256 of the font file, plus every non-deprecated icon with its codepoint, tags
    and aliases. The app reads the catalogs to offer the full icon sets in the symbol picker.

    Without -Update the script only checks: it compares each catalog version with the latest npm
    release and verifies that the font on disk still matches the recorded hash.

    With -Update it downloads the font and meta.json of the requested (default: latest) version from
    jsdelivr, replaces the font and regenerates the catalog.

.EXAMPLE
    pwsh tools/UpdateMdiFont/update-mdi-font.ps1
.EXAMPLE
    pwsh tools/UpdateMdiFont/update-mdi-font.ps1 -Update -Library mdi -Version 7.4.47
#>
param(
    [switch] $Update,
    [ValidateSet('all', 'mdi', 'mdil')]
    [string] $Library = 'all',
    [string] $Version
)

$ErrorActionPreference = 'Stop'

$fontDir = Join-Path $PSScriptRoot '..' '..' 'LoupixDeck' 'Assets' 'Fonts' | Resolve-Path

$libraries = @(
    [pscustomobject]@{
        Key         = 'mdi'
        Catalog     = 'mdi-catalog.json'
        FontPackage = '@mdi/font'
        MetaPackage = '@mdi/svg'
        FontFile    = 'materialdesignicons-webfont.ttf'
    }
    [pscustomobject]@{
        Key         = 'mdil'
        Catalog     = 'mdil-catalog.json'
        FontPackage = '@mdi/light-font'
        MetaPackage = '@mdi/light-svg'
        FontFile    = 'materialdesignicons-light-webfont.ttf'
    }
) | Where-Object { $Library -eq 'all' -or $_.Key -eq $Library }

function Get-LatestVersion([string] $package) {
    (Invoke-RestMethod "https://registry.npmjs.org/$package/latest").version
}

function Get-FileSha256([string] $path) {
    (Get-FileHash -Algorithm SHA256 -LiteralPath $path).Hash.ToLowerInvariant()
}

function ConvertTo-JsonString([string] $value) {
    ConvertTo-Json -InputObject $value -Compress
}

function ConvertTo-JsonStringArray([object[]] $values) {
    '[' + (($values | ForEach-Object { ConvertTo-JsonString $_ }) -join ',') + ']'
}

function Write-Catalog($lib, [string] $version, [string] $fontSha, [object[]] $meta) {
    # Written by hand, one icon per line, so the file is deterministic and diffs stay readable.
    $icons = $meta |
        Where-Object { -not $_.deprecated } |
        Sort-Object -Property name -Culture 'en-US' |
        ForEach-Object {
            $aliases = @($_.aliases | ForEach-Object { if ($_ -is [string]) { $_ } else { $_.name } })
            '    [' + (ConvertTo-JsonString $_.name) + ',' + (ConvertTo-JsonString $_.codepoint.ToUpperInvariant()) + ',' +
                (ConvertTo-JsonStringArray @($_.tags)) + ',' + (ConvertTo-JsonStringArray $aliases) + ']'
        }

    $lines = @(
        '{'
        '  "package": ' + (ConvertTo-JsonString $lib.FontPackage) + ','
        '  "version": ' + (ConvertTo-JsonString $version) + ','
        '  "fontFile": ' + (ConvertTo-JsonString $lib.FontFile) + ','
        '  "fontSha256": ' + (ConvertTo-JsonString $fontSha) + ','
        '  "icons": ['
        ($icons -join ",`n")
        '  ]'
        '}'
    )

    $path = Join-Path $fontDir $lib.Catalog
    [System.IO.File]::WriteAllText($path, ($lines -join "`n") + "`n", [System.Text.UTF8Encoding]::new($false))
    return @($icons).Count
}

$exitCode = 0

foreach ($lib in $libraries) {
    $catalogPath = Join-Path $fontDir $lib.Catalog
    $fontPath = Join-Path $fontDir $lib.FontFile
    $current = if (Test-Path $catalogPath) { (Get-Content -Raw $catalogPath | ConvertFrom-Json).version } else { $null }
    $latest = Get-LatestVersion $lib.FontPackage

    if (-not $Update) {
        if (-not $current) {
            Write-Host "$($lib.Key): no catalog found - run with -Update"
            $exitCode = 1
            continue
        }

        $catalog = Get-Content -Raw $catalogPath | ConvertFrom-Json
        if (-not (Test-Path $fontPath) -or (Get-FileSha256 $fontPath) -ne $catalog.fontSha256) {
            Write-Host "$($lib.Key): font file does not match the catalog hash - run with -Update -Version $current"
            $exitCode = 1
        }

        if ($current -eq $latest) {
            Write-Host "$($lib.Key): $current up to date"
        }
        else {
            Write-Host "$($lib.Key): update available $current -> $latest"
            $exitCode = 1
        }
        continue
    }

    $target = if ($Version) { $Version } else { $latest }
    Write-Host "$($lib.Key): installing $($lib.FontPackage)@$target (was $current)"

    $fontUrl = "https://cdn.jsdelivr.net/npm/$($lib.FontPackage)@$target/fonts/$($lib.FontFile)"
    $metaUrl = "https://cdn.jsdelivr.net/npm/$($lib.MetaPackage)@$target/meta.json"

    $tempFont = [System.IO.Path]::GetTempFileName()
    try {
        Invoke-WebRequest -Uri $fontUrl -OutFile $tempFont
        $meta = Invoke-RestMethod -Uri $metaUrl
        Move-Item -LiteralPath $tempFont -Destination $fontPath -Force
    }
    finally {
        if (Test-Path $tempFont) { Remove-Item -LiteralPath $tempFont }
    }

    $count = Write-Catalog $lib $target (Get-FileSha256 $fontPath) $meta
    Write-Host "$($lib.Key): wrote $($lib.Catalog) with $count icons"
}

exit $exitCode
