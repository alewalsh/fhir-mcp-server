# Loads Synthea FHIR R4 bundles into a local HAPI server, in reference order:
# hospitalInformation* -> practitionerInformation* -> patient bundles (batched).
#
# Defaults to a small patient limit - full 555-bundle loads are slow and heap-heavy.
# Examples:
#   .\data\load-synthea.ps1
#   .\data\load-synthea.ps1 -Limit 100 -BatchSize 10 -BatchPauseSeconds 15
#   .\data\load-synthea.ps1 -Limit 0
#   .\data\load-synthea.ps1 -Skip 52 -Limit 48 -SkipOrgBundles
param(
    # Prefer 127.0.0.1 on Windows - localhost often resolves to ::1 and hangs.
    [string]$FhirBaseUrl = "http://127.0.0.1:8080/fhir",
    [string]$DataDir = (Join-Path $PSScriptRoot "sample-synthea-data"),
    [int]$Limit = 50,
    [int]$BatchSize = 10,
    [int]$BatchPauseSeconds = 15,
    [int]$Skip = 0,
    [int]$CurlMaxTimeSeconds = 180,
    [int]$ReadyTimeoutSeconds = 180,
    [switch]$SkipOrgBundles
)

$ErrorActionPreference = "Stop"

if (-not (Test-Path $DataDir)) {
    throw "Data directory not found: $DataDir"
}
if ($Limit -lt 0) { throw "Limit must be >= 0 (0 = all patients)" }
if ($BatchSize -lt 1) { throw "BatchSize must be >= 1" }
if ($Skip -lt 0) { throw "Skip must be >= 0" }

function Test-HapiReady {
    $code = & curl.exe -s -o NUL -w "%{http_code}" --max-time 5 "$FhirBaseUrl/metadata"
    return ($code -eq "200")
}

function Wait-HapiReady {
    param(
        [int]$TimeoutSeconds = $ReadyTimeoutSeconds,
        [string]$Reason = "HAPI readiness"
    )

    Write-Host "Waiting for HAPI ($Reason, up to ${TimeoutSeconds}s)..." -ForegroundColor DarkGray
    $deadline = (Get-Date).AddSeconds($TimeoutSeconds)
    while ((Get-Date) -lt $deadline) {
        if (Test-HapiReady) {
            Write-Host "HAPI ready." -ForegroundColor DarkGray
            return $true
        }
        Start-Sleep -Seconds 5
    }
    return $false
}

function Post-Bundle {
    param(
        [Parameter(Mandatory)][string]$Path,
        [int]$Retries = 1
    )

    $name = Split-Path $Path -Leaf
    Write-Host "POST $name" -NoNewline

    $attempt = 0
    while ($true) {
        $attempt++
        $tmp = [System.IO.Path]::GetTempFileName()
        try {
            $status = & curl.exe -s -o $tmp -w "%{http_code}" --max-time $CurlMaxTimeSeconds `
                -X POST $FhirBaseUrl `
                -H "Content-Type: application/fhir+json" `
                --data-binary "@$Path"

            if ($status -in @("200", "201")) {
                Write-Host " -> HTTP $status" -ForegroundColor Green
                return
            }

            $body = Get-Content $tmp -Raw -ErrorAction SilentlyContinue
            $oom = $body -match "OutOfMemoryError|Java heap space"
            $down = ($status -eq "000") -or ($status -eq "")

            if (($oom -or $down) -and $attempt -le ($Retries + 1)) {
                Write-Host " -> HTTP $status (server stressed/down) - waiting for recovery" -ForegroundColor Yellow
                if (-not (Wait-HapiReady -Reason "post-OOM/recovery")) {
                    throw "HAPI did not recover after HTTP $status posting $name. Recreate: docker compose up -d --force-recreate"
                }
                continue
            }

            Write-Host " -> HTTP $status" -ForegroundColor Red
            throw "Failed posting $name (HTTP $status). Body: $body"
        }
        finally {
            Remove-Item $tmp -ErrorAction SilentlyContinue
        }
    }
}

function Write-ResumeTip {
    param([int]$ResumeSkip)

    Write-Host ""
    Write-Host "If HAPI is dead/wedged:" -ForegroundColor Yellow
    Write-Host "  docker compose up -d --force-recreate" -ForegroundColor Yellow
    Write-Host "  (wait until metadata returns 200, ~40-60s)" -ForegroundColor Yellow
    Write-Host "Then either reload cleanly, or resume only if the SAME container survived:" -ForegroundColor Yellow
    Write-Host "  .\data\load-synthea.ps1 -Skip $ResumeSkip -Limit $Limit -BatchSize $BatchSize -SkipOrgBundles" -ForegroundColor Yellow
    Write-Host "Note: recreate wipes in-container data - start from Skip=0 without -SkipOrgBundles." -ForegroundColor Yellow
}

if (-not (Wait-HapiReady -Reason "startup check")) {
    throw "HAPI is not reachable at $FhirBaseUrl/metadata. Start it with: docker compose up -d"
}

$hospital = @(Get-ChildItem -Path $DataDir -Filter "hospitalInformation*.json" -File)
$practitioner = @(Get-ChildItem -Path $DataDir -Filter "practitionerInformation*.json" -File)
$hasOrgBundles = ($hospital.Count -gt 0) -and ($practitioner.Count -gt 0)
$loadOrgBundles = $hasOrgBundles -and -not $SkipOrgBundles

# Official Synthea sample ZIP has patient bundles only (no hospital/practitioner files).
if (-not $hasOrgBundles -and -not $SkipOrgBundles) {
    Write-Host "No hospitalInformation*/practitionerInformation* in $DataDir - skipping org load (typical for official sample ZIP)." -ForegroundColor DarkYellow
}

# ponytail: wrap in @() - Get-ChildItem returns a scalar FileInfo for a single match
$skipPaths = @($hospital | ForEach-Object { $_.FullName }) + @($practitioner | ForEach-Object { $_.FullName })
$allPatients = @(Get-ChildItem -Path $DataDir -Filter "*.json" -File |
    Where-Object { $skipPaths -notcontains $_.FullName } |
    Sort-Object Name)
if ($allPatients.Count -eq 0) {
    throw "No patient *.json bundles found in $DataDir (see data/README.md - extract fhir/*.json from the sample ZIP)"
}

if ($Skip -ge $allPatients.Count) {
    throw "Skip=$Skip is past the end of $($allPatients.Count) patient files"
}

$patients = $allPatients | Select-Object -Skip $Skip
if ($Limit -gt 0) {
    $patients = @($patients | Select-Object -First $Limit)
}
else {
    $patients = @($patients)
}

$limitLabel = if ($Limit -eq 0) { "all" } else { "$Limit" }
Write-Host "FHIR: $FhirBaseUrl"
Write-Host "Data: $DataDir"
Write-Host "Patients: loading $($patients.Count) of $($allPatients.Count) (Skip=$Skip Limit=$limitLabel BatchSize=$BatchSize Pause=${BatchPauseSeconds}s)"
Write-Host ""

if ($loadOrgBundles) {
    Write-Host "=== 1/3 Hospital ==="
    Post-Bundle $hospital[0].FullName

    Write-Host ""
    Write-Host "=== 2/3 Practitioner ==="
    Post-Bundle $practitioner[0].FullName
}
else {
    Write-Host "=== 1-2/3 Hospital + Practitioner === skipped"
}

Write-Host ""
Write-Host "=== 3/3 Patients ($($patients.Count)) ==="

$ok = 0
$failed = @()
$i = 0
foreach ($patient in $patients) {
    $i++
    Write-Host "[$i/$($patients.Count)] " -NoNewline
    try {
        Post-Bundle $patient.FullName
        $ok++
    }
    catch {
        Write-Host $_.Exception.Message -ForegroundColor Red
        $failed += $patient.Name

        # Connection dead / unrecovered OOM: stop immediately instead of cascading 000s.
        if ($_.Exception.Message -match "HTTP 000|did not recover|OutOfMemoryError|Java heap space") {
            Write-Host ""
            Write-Host "Aborting load after server failure at patient index $($Skip + $i) ($($patient.Name))." -ForegroundColor Red
            Write-ResumeTip -ResumeSkip ($Skip + $ok)
            exit 1
        }
    }

    if (($i % $BatchSize) -eq 0 -and $i -lt $patients.Count) {
        Write-Host "--- batch pause $BatchPauseSeconds s (heap cooldown) ---" -ForegroundColor DarkGray
        Start-Sleep -Seconds $BatchPauseSeconds
        if (-not (Test-HapiReady)) {
            if (-not (Wait-HapiReady -Reason "batch pause")) {
                Write-Host "HAPI died during batch pause." -ForegroundColor Red
                Write-ResumeTip -ResumeSkip ($Skip + $ok)
                exit 1
            }
        }
    }
}

Write-Host ""
Write-Host "Done. Patients ok=$ok failed=$($failed.Count)"
if ($failed.Count -gt 0) {
    Write-Host "Failed files:" -ForegroundColor Red
    $failed | ForEach-Object { Write-Host "  $_" -ForegroundColor Red }
    Write-ResumeTip -ResumeSkip ($Skip + $ok)
    exit 1
}
