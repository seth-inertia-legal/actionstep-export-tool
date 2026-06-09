# CLI Reference

## Build and run

```bash
cd ActionstepDeltaExport
dotnet run -- <command> [options]
```

---

## Commands

### export

Exports one or more data types from Actionstep.

```
dotnet run -- export --include <values> [--download] [--since <date>] [--manifest <path>] [--output <path>]
```

#### Options

| Option | Type | Required | Description |
|--------|------|----------|-------------|
| `--include` | string | Yes | Comma-separated list of what to export. Valid values: `documents`, `actions`, `folders`. At least one required. |
| `--download` | flag | No | Download document files to disk. Without this flag, only the audit CSV is written. Applies to `documents` only. |
| `--since` | string | No | Override `SinceDate` from `appsettings.Local.json`. ISO date, e.g. `2026-05-23`. Applies to `documents` only. |
| `--manifest` | string | No | Override `ManifestPath` from `appsettings.Local.json`. |
| `--output` | string | No | Override `OutputRoot` from `appsettings.Local.json`. |

#### --include values

- `documents` — streams `actiondocuments` modified since `SinceDate`, upserts into the manifest CSV
- `actions` — full dump of all `actions` (matters) to a CSV
- `folders` — full dump of all `actionfolders` to a CSV with resolved paths

Multiple values are comma-separated with no spaces: `--include documents,actions,folders`

#### SinceDate derivation (documents only)

If `--since` is not passed and `SinceDate` is not set in config, the tool derives
the cutoff from the maximum `last_modified` value in the existing manifest.
If no manifest exists and no date is provided, the run errors out.

#### Exit codes

| Code | Meaning |
|------|---------|
| 0 | Success |
| 1 | Configuration error (bad date, missing required value) |
| 2 | Partial success — some document downloads failed (errors CSV written) |
| 99 | Unhandled exception |

---

### probe

Fetches one raw `actiondocuments` record and prints pretty-printed JSON.
Use to verify field name mappings before the first export.

```
dotnet run -- probe
```

No options. Authenticates and prints to stdout.

---

## Usage examples

### Full audit — all documents from 2000 (no download)

```
dotnet run -- export --include documents --since 2000-01-01
```

### Delta audit — documents since last run (derives date from manifest)

```
dotnet run -- export --include documents --manifest D:\Export\manifest.csv
```

### Download delta documents

```
dotnet run -- export --include documents --since 2026-05-01 --download
```

### Actions and folders dump only

```
dotnet run -- export --include actions,folders
```

### Full combined run with download

```
dotnet run -- export --include documents,actions,folders --since 2026-05-01 --download
```

### Override output directory on the fly

```
dotnet run -- export --include folders --output "D:\Export June"
```

---

## Output file naming

| File | Condition |
|------|-----------|
| `documents_audit_{yyyyMMdd_HHmmss}.csv` | `--include documents`, no `--download` |
| `documents_{yyyyMMdd_HHmmss}.csv` | `--include documents --download` |
| `actions_{yyyyMMdd_HHmmss}.csv` | `--include actions` |
| `folders_{yyyyMMdd_HHmmss}.csv` | `--include folders` |
| `errors_{yyyyMMdd_HHmmss}.csv` | `--include documents --download`, only if download errors occur |

All files are written to `OutputRoot` (from config or `--output`).
Downloaded files are written to `OutputRoot/{action_id}/{filename}`.

---

## Progress output (documents)

While streaming documents, a progress line is logged every 100 documents:

```
  [AUDIT] 200/4821 docs  |  12 action(s) resolved  |  elapsed 1m 23s  |  ETA 14m 02s
```

The first page fetch logs the total document count and page count:

```
  → 4821 document(s) in delta across 25 page(s).
```
