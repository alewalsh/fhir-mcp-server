# Synthea data (local only)

Put Synthea-generated FHIR R4 JSON bundles in `sample-synthea-data/` next to this file.

**Never commit patient payloads** — even synthetic dumps. They are gitignored
(`data/**/*.json`, `data/**/*.ndjson`, `data/**/*.xml`, and `sample-synthea-data/`).

## Obtain (ZIP first — no Java)

Official prebuilt FHIR R4 sample from [Synthea sample data](https://synthetichealth.github.io/synthea/)
(~1k patients, ~85 MB zip). Extract the `fhir/*.json` files into `sample-synthea-data/`.

**URL:**
`https://synthetichealth.github.io/synthea-sample-data/downloads/synthea_sample_data_fhir_r4_sep2019.zip`

```bash
# macOS / Linux
mkdir -p data/sample-synthea-data
curl -L -o /tmp/synthea_fhir_r4.zip \
  "https://synthetichealth.github.io/synthea-sample-data/downloads/synthea_sample_data_fhir_r4_sep2019.zip"
unzip -o /tmp/synthea_fhir_r4.zip -d /tmp/synthea_fhir_r4
cp /tmp/synthea_fhir_r4/fhir/*.json data/sample-synthea-data/
```

```powershell
# Windows
New-Item -ItemType Directory -Force -Path data\sample-synthea-data | Out-Null
curl.exe -L -o $env:TEMP\synthea_fhir_r4.zip `
  "https://synthetichealth.github.io/synthea-sample-data/downloads/synthea_sample_data_fhir_r4_sep2019.zip"
Expand-Archive -Force $env:TEMP\synthea_fhir_r4.zip $env:TEMP\synthea_fhir_r4
Copy-Item $env:TEMP\synthea_fhir_r4\fhir\*.json data\sample-synthea-data\
```

**Layout note:** the zip root is `fhir/*.json` only. It does **not** include
`hospitalInformation*` / `practitionerInformation*` bundles. The loaders detect
that and skip the org step automatically (dangling Organization/Practitioner
references in patient bundles are fine for local HAPI demos).

## Alternative — generate with Java (custom set)

If you want a fresher or smaller population (and hospital/practitioner files),
install a JDK and follow [Synthea’s run instructions](https://github.com/synthetichealth/synthea).
Copy `output/fhir/*.json` into `sample-synthea-data/`. Not required for Quick start.

## Loaders (dev setup only — not MCP tools)

These scripts POST bundles into local HAPI (hospital → practitioner → patients when present).
They are **not** part of the MCP tool surface.

| OS | Command |
| --- | --- |
| Windows | `powershell -ExecutionPolicy Bypass -File .\data\load-synthea.ps1` |
| macOS / Linux | `chmod +x data/load-synthea.sh && ./data/load-synthea.sh` |

Defaults: 50 patients, batches of 10 with 15s pause, FHIR base `http://127.0.0.1:8080/fhir`.

```bash
# Unix examples
./data/load-synthea.sh --limit 100 --batch-size 10 --batch-pause 15
./data/load-synthea.sh --skip 52 --limit 48 --skip-org-bundles
```

```powershell
# Windows examples
.\data\load-synthea.ps1 -Limit 100 -BatchSize 10 -BatchPauseSeconds 15
.\data\load-synthea.ps1 -Skip 52 -Limit 48 -SkipOrgBundles
```

Start HAPI first: `docker compose up -d`.
