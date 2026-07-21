#!/usr/bin/env bash
# Loads Synthea FHIR R4 bundles into a local HAPI server, in reference order:
# hospitalInformation* -> practitionerInformation* -> patient bundles (batched).
#
# Parity with load-synthea.ps1. On Windows prefer the PowerShell script.
# Compatible with bash 3.2+ (macOS /bin/bash).
#
# Examples:
#   ./data/load-synthea.sh
#   ./data/load-synthea.sh --limit 100 --batch-size 10 --batch-pause 15
#   ./data/load-synthea.sh --limit 0
#   ./data/load-synthea.sh --skip 52 --limit 48 --skip-org-bundles
set -euo pipefail

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

FhirBaseUrl="http://127.0.0.1:8080/fhir"
DataDir="${SCRIPT_DIR}/sample-synthea-data"
Limit=50
BatchSize=10
BatchPauseSeconds=15
Skip=0
CurlMaxTimeSeconds=180
ReadyTimeoutSeconds=180
SkipOrgBundles=0

usage() {
  cat <<'EOF'
Usage: load-synthea.sh [options]

  --fhir-base-url URL   FHIR base (default: http://127.0.0.1:8080/fhir)
  --data-dir PATH       Synthea JSON dir (default: ./sample-synthea-data next to script)
  --limit N             Max patients (0 = all; default 50)
  --batch-size N        Pause every N patient posts (default 10)
  --batch-pause SEC     Seconds between batches (default 15)
  --skip N              Skip first N patient files (default 0)
  --curl-max-time SEC   curl --max-time per POST (default 180)
  --ready-timeout SEC   Wait for HAPI metadata (default 180)
  --skip-org-bundles    Skip hospital/practitioner posts
  -h, --help            Show this help
EOF
}

while [ $# -gt 0 ]; do
  case "$1" in
    --fhir-base-url) FhirBaseUrl="$2"; shift 2 ;;
    --data-dir) DataDir="$2"; shift 2 ;;
    --limit) Limit="$2"; shift 2 ;;
    --batch-size) BatchSize="$2"; shift 2 ;;
    --batch-pause) BatchPauseSeconds="$2"; shift 2 ;;
    --skip) Skip="$2"; shift 2 ;;
    --curl-max-time) CurlMaxTimeSeconds="$2"; shift 2 ;;
    --ready-timeout) ReadyTimeoutSeconds="$2"; shift 2 ;;
    --skip-org-bundles) SkipOrgBundles=1; shift ;;
    -h|--help) usage; exit 0 ;;
    *) echo "Unknown option: $1" >&2; usage >&2; exit 1 ;;
  esac
done

if [ ! -d "$DataDir" ]; then
  echo "Data directory not found: $DataDir" >&2
  exit 1
fi
if [ "$Limit" -lt 0 ]; then
  echo "Limit must be >= 0 (0 = all patients)" >&2
  exit 1
fi
if [ "$BatchSize" -lt 1 ]; then
  echo "BatchSize must be >= 1" >&2
  exit 1
fi
if [ "$Skip" -lt 0 ]; then
  echo "Skip must be >= 0" >&2
  exit 1
fi

hapi_ready() {
  local code
  code="$(curl -s -o /dev/null -w "%{http_code}" --max-time 5 "${FhirBaseUrl}/metadata" || true)"
  [ "$code" = "200" ]
}

wait_hapi_ready() {
  local reason="${1:-HAPI readiness}"
  local timeout="$ReadyTimeoutSeconds"
  local elapsed=0
  echo "Waiting for HAPI ($reason, up to ${timeout}s)..."
  while [ "$elapsed" -lt "$timeout" ]; do
    if hapi_ready; then
      echo "HAPI ready."
      return 0
    fi
    sleep 5
    elapsed=$((elapsed + 5))
  done
  return 1
}

write_resume_tip() {
  local resume_skip="$1"
  echo ""
  echo "If HAPI is dead/wedged:"
  echo "  docker compose up -d --force-recreate"
  echo "  (wait until metadata returns 200, ~40-60s)"
  echo "Then either reload cleanly, or resume only if the SAME container survived:"
  echo "  ./data/load-synthea.sh --skip ${resume_skip} --limit ${Limit} --batch-size ${BatchSize} --skip-org-bundles"
  echo "Note: recreate wipes in-container data - start from --skip 0 without --skip-org-bundles."
}

post_bundle() {
  local path="$1"
  local retries="${2:-1}"
  local name
  name="$(basename "$path")"
  printf "POST %s" "$name"

  local attempt=0
  while true; do
    attempt=$((attempt + 1))
    local tmp
    tmp="$(mktemp)"
    local status
    status="$(curl -s -o "$tmp" -w "%{http_code}" --max-time "$CurlMaxTimeSeconds" \
      -X POST "$FhirBaseUrl" \
      -H "Content-Type: application/fhir+json" \
      --data-binary @"$path" || true)"

    if [ "$status" = "200" ] || [ "$status" = "201" ]; then
      echo " -> HTTP $status"
      rm -f "$tmp"
      return 0
    fi

    local body
    body="$(cat "$tmp" 2>/dev/null || true)"
    rm -f "$tmp"

    local oom=0 down=0
    if echo "$body" | grep -Eq "OutOfMemoryError|Java heap space"; then
      oom=1
    fi
    if [ "$status" = "000" ] || [ -z "$status" ]; then
      down=1
    fi

    if { [ "$oom" -eq 1 ] || [ "$down" -eq 1 ]; } && [ "$attempt" -le $((retries + 1)) ]; then
      echo " -> HTTP $status (server stressed/down) - waiting for recovery"
      if ! wait_hapi_ready "post-OOM/recovery"; then
        echo "HAPI did not recover after HTTP $status posting $name. Recreate: docker compose up -d --force-recreate" >&2
        return 1
      fi
      continue
    fi

    echo " -> HTTP $status"
    echo "Failed posting $name (HTTP $status). Body: $body" >&2
    return 1
  done
}

if ! wait_hapi_ready "startup check"; then
  echo "HAPI is not reachable at ${FhirBaseUrl}/metadata. Start it with: docker compose up -d" >&2
  exit 1
fi

hospital=""
practitioner=""
for f in "$DataDir"/hospitalInformation*.json; do
  [ -f "$f" ] || continue
  hospital="$f"
  break
done
for f in "$DataDir"/practitionerInformation*.json; do
  [ -f "$f" ] || continue
  practitioner="$f"
  break
done

# Official Synthea sample ZIP has patient bundles only (no hospital/practitioner files).
if [ -z "$hospital" ] || [ -z "$practitioner" ]; then
  if [ "$SkipOrgBundles" -eq 0 ]; then
    echo "No hospitalInformation*/practitionerInformation* in $DataDir — skipping org load (typical for official sample ZIP)." >&2
  fi
  SkipOrgBundles=1
fi

# Collect patient JSON paths (exclude hospital/practitioner), sorted.
patient_list="$(mktemp)"
trap 'rm -f "$patient_list"' EXIT
for f in "$DataDir"/*.json; do
  [ -f "$f" ] || continue
  case "$(basename "$f")" in
    hospitalInformation*.json|practitionerInformation*.json) continue ;;
  esac
  printf '%s\n' "$f"
done | sort > "$patient_list"

patient_total="$(wc -l < "$patient_list" | tr -d ' ')"
if [ "$patient_total" -eq 0 ]; then
  echo "No patient *.json bundles found in $DataDir (see data/README.md — extract fhir/*.json from the sample ZIP)" >&2
  exit 1
fi
if [ "$Skip" -ge "$patient_total" ]; then
  echo "Skip=$Skip is past the end of $patient_total patient files" >&2
  exit 1
fi

# Slice: skip first Skip lines, then take Limit (or all if Limit=0).
work_list="$(mktemp)"
trap 'rm -f "$patient_list" "$work_list"' EXIT
tail -n +"$((Skip + 1))" "$patient_list" > "$work_list"
if [ "$Limit" -gt 0 ]; then
  head -n "$Limit" "$work_list" > "${work_list}.tmp"
  mv "${work_list}.tmp" "$work_list"
fi

patient_count="$(wc -l < "$work_list" | tr -d ' ')"
limit_label="$Limit"
if [ "$Limit" -eq 0 ]; then
  limit_label="all"
fi

echo "FHIR: $FhirBaseUrl"
echo "Data: $DataDir"
echo "Patients: loading ${patient_count} of ${patient_total} (Skip=$Skip Limit=$limit_label BatchSize=$BatchSize Pause=${BatchPauseSeconds}s)"
echo ""

if [ "$SkipOrgBundles" -eq 0 ]; then
  echo "=== 1/3 Hospital ==="
  post_bundle "$hospital"

  echo ""
  echo "=== 2/3 Practitioner ==="
  post_bundle "$practitioner"
else
  echo "=== 1-2/3 Hospital + Practitioner === skipped (--skip-org-bundles)"
fi

echo ""
echo "=== 3/3 Patients (${patient_count}) ==="

ok=0
failed_names=""
i=0
while IFS= read -r patient || [ -n "$patient" ]; do
  [ -n "$patient" ] || continue
  i=$((i + 1))
  printf "[%s/%s] " "$i" "$patient_count"
  set +e
  post_bundle "$patient"
  rc=$?
  set -e
  if [ "$rc" -eq 0 ]; then
    ok=$((ok + 1))
  else
    base="$(basename "$patient")"
    failed_names="${failed_names}${base}"$'\n'
    # Abort on hard server failure (message already printed by post_bundle).
    if ! hapi_ready; then
      echo ""
      echo "Aborting load after server failure at patient index $((Skip + i)) ($base)." >&2
      write_resume_tip $((Skip + ok))
      exit 1
    fi
  fi

  if [ $((i % BatchSize)) -eq 0 ] && [ "$i" -lt "$patient_count" ]; then
    echo "--- batch pause ${BatchPauseSeconds} s (heap cooldown) ---"
    sleep "$BatchPauseSeconds"
    if ! hapi_ready; then
      if ! wait_hapi_ready "batch pause"; then
        echo "HAPI died during batch pause." >&2
        write_resume_tip $((Skip + ok))
        exit 1
      fi
    fi
  fi
done < "$work_list"

failed_count=0
if [ -n "$failed_names" ]; then
  failed_count="$(printf '%s' "$failed_names" | grep -c . || true)"
fi

echo ""
echo "Done. Patients ok=$ok failed=$failed_count"
if [ "$failed_count" -gt 0 ]; then
  echo "Failed files:" >&2
  printf '%s' "$failed_names" | while IFS= read -r f || [ -n "$f" ]; do
    [ -n "$f" ] && echo "  $f" >&2
  done
  write_resume_tip $((Skip + ok))
  exit 1
fi
