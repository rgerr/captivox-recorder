param(
    [Parameter(Mandatory=$false)]
    [string]$Path,

    [Parameter(Mandatory=$false)]
    [string]$ImportFolder,

    [Parameter(Mandatory=$false)]
    [string]$FfprobePath = "ffprobe.exe",

    [Parameter(Mandatory=$false)]
    [switch]$Json
)

Set-StrictMode -Version Latest
$ErrorActionPreference = 'Stop'

function Convert-ToDateTimeOffsetOrNull {
    param([string]$Value)
    if ([string]::IsNullOrWhiteSpace($Value)) { return $null }
    $dto = [DateTimeOffset]::MinValue
    if ([DateTimeOffset]::TryParse($Value, [Globalization.CultureInfo]::InvariantCulture, [Globalization.DateTimeStyles]::AssumeUniversal, [ref]$dto)) {
        return $dto
    }
    if ([DateTimeOffset]::TryParse($Value, [Globalization.CultureInfo]::CurrentCulture, [Globalization.DateTimeStyles]::AssumeLocal, [ref]$dto)) {
        return $dto
    }
    return $null
}

function Test-PlausibleRecordedAt {
    param([Nullable[DateTimeOffset]]$Candidate, [Nullable[DateTimeOffset]]$ImportedAt)
    if ($null -eq $Candidate) { return $false }
    if ($Candidate.Value.Year -lt 2000 -or $Candidate.Value.Year -gt ((Get-Date).Year + 1)) { return $false }
    if ($null -ne $ImportedAt -and $Candidate.Value -gt $ImportedAt.Value.AddMinutes(10)) { return $false }
    return $true
}

function Test-LikelyImportOrSyncTime {
    param([Nullable[DateTimeOffset]]$Candidate, [Nullable[DateTimeOffset]]$ImportedAt)
    if ($null -eq $Candidate -or $null -eq $ImportedAt) { return $false }
    return ([TimeSpan]($ImportedAt.Value - $Candidate.Value)).Duration() -le [TimeSpan]::FromMinutes(15)
}

function Get-EstimatedStartFromModifiedAt {
    param([Nullable[DateTimeOffset]]$ModifiedAt, [Nullable[Double]]$DurationSeconds)
    if ($null -eq $ModifiedAt -or $null -eq $DurationSeconds -or $DurationSeconds.Value -le 0) { return $null }
    return $ModifiedAt.Value.AddSeconds(-1 * $DurationSeconds.Value)
}

function Read-FfprobeMetadata {
    param([string]$File)
    $result = [ordered]@{ duration = $null; embedded_recorded_at = $null }
    try {
        $raw = & $FfprobePath -v quiet -print_format json -show_format -show_streams -- $File 2>$null
        if ([string]::IsNullOrWhiteSpace($raw)) { return $result }
        $probe = $raw | ConvertFrom-Json
        if ($probe.format.duration) {
            $durationValue = 0.0
            if ([double]::TryParse([string]$probe.format.duration, [Globalization.NumberStyles]::Float, [Globalization.CultureInfo]::InvariantCulture, [ref]$durationValue)) {
                $result.duration = $durationValue
            }
        }
        $tagCandidates = @()
        if ($probe.format.tags) { $tagCandidates += $probe.format.tags }
        if ($probe.streams) {
            foreach ($stream in $probe.streams) {
                if ($stream.tags) { $tagCandidates += $stream.tags }
            }
        }
        foreach ($tags in $tagCandidates) {
            foreach ($name in @('creation_time','com.apple.quicktime.creationdate','date')) {
                $prop = $tags.PSObject.Properties[$name]
                if ($prop -and $prop.Value) {
                    $dt = Convert-ToDateTimeOffsetOrNull ([string]$prop.Value)
                    if ($dt) {
                        $result.embedded_recorded_at = $dt
                        return $result
                    }
                }
            }
        }
    }
    catch {
        # Defensief: metadata-inspectie mag nooit falen op ontbrekende ffprobe of ontbrekende tags.
    }
    return $result
}

function Inspect-File {
    param([System.IO.FileInfo]$File)
    $importedAt = [DateTimeOffset](Get-Date)
    $probe = Read-FfprobeMetadata -File $File.FullName
    $embedded = $probe.embedded_recorded_at
    $created = [DateTimeOffset]$File.CreationTime
    $modified = [DateTimeOffset]$File.LastWriteTime

    $confidence = 'unknown'
    $source = 'none'
    $recommended = $null
    $reason = 'Geen bruikbare tijdinformatie gevonden.'

    $modifiedMinusDuration = Get-EstimatedStartFromModifiedAt -ModifiedAt $modified -DurationSeconds $probe.duration

    if (Test-PlausibleRecordedAt -Candidate $embedded -ImportedAt $importedAt) {
        $confidence = 'high'
        $source = 'embedded_audio_metadata'
        $recommended = $embedded
        $reason = 'Interne audio metadata bevat een plausibele originele opnametijd.'
    }
    elseif ((Test-PlausibleRecordedAt -Candidate $created -ImportedAt $importedAt) -and -not (Test-LikelyImportOrSyncTime -Candidate $created -ImportedAt $importedAt)) {
        $confidence = 'medium'
        $source = 'filesystem_created_at'
        $recommended = $created
        $reason = 'Filesystem aanmaaktijd ligt voldoende los van het import-/syncmoment en lijkt plausibel, maar is niet bewezen als originele opnamestart.'
    }
    elseif ((Test-PlausibleRecordedAt -Candidate $modifiedMinusDuration -ImportedAt $importedAt) -and -not (Test-LikelyImportOrSyncTime -Candidate $modified -ImportedAt $importedAt)) {
        $confidence = 'medium'
        $source = 'filesystem_modified_at_minus_duration'
        $recommended = $modifiedMinusDuration
        $reason = 'Filesystem wijzigingstijd ligt voldoende los van het import-/syncmoment; opnamestart is defensief afgeleid als modified_at minus duration.'
    }
    else {
        $confidence = 'low'
        $source = 'imported_at_fallback_only'
        $recommended = $null
        $reason = 'Alleen import-/syncmoment of daarmee samenvallende filesystemtijd beschikbaar. Niet gebruiken als harde agenda-matchtijd.'
    }

    [pscustomobject]@{
        filename = $File.Name
        path = $File.FullName
        duration = $probe.duration
        embedded_recorded_at = $embedded
        file_created_at = $created
        file_modified_at = $modified
        onedrive_local_sync_time = $null
        imported_at = $importedAt
        metadata_confidence = $confidence
        recommended_recorded_at = $recommended
        recommended_recorded_at_source = $source
        recommended_agenda_match_time = $(if ($confidence -in @('high','medium')) { $recommended } else { $null })
        confidence_reason = $reason
    }
}

$targets = @()
if (-not [string]::IsNullOrWhiteSpace($Path)) {
    $item = Get-Item -LiteralPath $Path
    if ($item.PSIsContainer) { $ImportFolder = $item.FullName } else { $targets += $item }
}
if (-not [string]::IsNullOrWhiteSpace($ImportFolder)) {
    $extensions = @('.m4a','.mp3','.wav','.aac','.mp4','.m4v')
    $targets += Get-ChildItem -LiteralPath $ImportFolder -File | Where-Object { $extensions -contains $_.Extension.ToLowerInvariant() }
}
if ($targets.Count -eq 0) {
    throw "Geen audiobestanden gevonden. Gebruik -Path <bestand|map> of -ImportFolder <map>."
}

$result = $targets | Sort-Object FullName | ForEach-Object { Inspect-File -File $_ }
if ($Json) {
    $result | ConvertTo-Json -Depth 5
} else {
    $result | Format-Table filename, duration, embedded_recorded_at, file_created_at, file_modified_at, imported_at, metadata_confidence, recommended_recorded_at, recommended_agenda_match_time -AutoSize
}
