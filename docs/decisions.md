# Design Decisions

Key choices made during development, with rationale. Read this before changing
any of these patterns — the alternatives were considered and rejected.

---

## `checkedOutTo` as `modified_by`

**Decision:** Use the `checkedOutTo` string field on `actiondocuments` as the
`modified_by` value in the manifest, falling back to `createdBy` if null.

**Why:** The Actionstep `actiondocuments` API exposes no `modifiedBy` participant
link. `checkedOutTo` is the closest available proxy — it identifies who last
interacted with the document. If the document was never checked out, `createdBy`
(resolved via participant lookup) is used instead.

---

## Per-action lazy folder cache (documents export)

**Decision:** Folder data is fetched per action on demand, not upfront for the
whole org, during the documents export.

**Why:** A delta run typically affects a small number of actions. Fetching all
folders upfront for a full-org run would be expensive and slow. The lazy cache
(`folderCache` dictionary in `ExportDocumentsAsync`) means folders are only
fetched for actions whose documents appear in the current delta.

For a full `--include folders` dump, `GetAllFoldersAsync` is used instead,
which fetches all folders in a single paginated pass without an action filter.

---

## `--include` / `--download` CLI design

**Decision:** Use `--include documents,actions,folders` (required, comma-separated)
and `--download` (optional flag) instead of the earlier `--dry-run` flag or a
`--mode [export|audit]` design.

**Why:**
- `--include` makes the intent explicit — you always know exactly what data will
  be touched, and combining types in one run is natural.
- `--download` as a separate flag makes the "audit only" case the safe default.
  Forgetting the flag can't accidentally download thousands of files.
- Actions and folders have no concept of "dry run" — they always write a CSV —
  so tying their export to a dry-run mode would have been confusing.

---

## Manifest upsert pattern

**Decision:** Load the existing manifest into a dictionary, upsert delta records
into it, and write a new timestamped CSV. Never overwrite the original manifest file.

**Why:** Preserves history and allows recovery if a run produces bad data.
The original manifest is always left intact. The merged output is a new file.
`HeaderValidated = null` in the CsvHelper config allows reading older manifests
that lack the `folder_path` column without errors.

---

## `SinceDate` derivation from manifest

**Decision:** If `SinceDate` is not configured and no `--since` flag is passed,
derive it from `max(last_modified)` across all records in the existing manifest.

**Why:** Avoids requiring the operator to manually track the last run date. For
the common case of a recurring delta run, the manifest already contains the
information needed to compute the correct cutoff.

---

## `appsettings.Local.json` for credentials

**Decision:** Real credentials live only in `appsettings.Local.json`, which is
gitignored. `appsettings.json` in the repo contains only `YOUR_CLIENT_ID` /
`YOUR_CLIENT_SECRET` placeholders.

**Why:** Prevents credentials from ever being accidentally committed. Each machine
(dev VM, client server) maintains its own Local file. The configuration layer
merges them at startup: Local values override base values.

---

## `SingleOrArrayConverter<T>`

**Decision:** A custom `JsonConverter<List<T>>` that handles both single-object
and array JSON responses for the same field.

**Why:** Actionstep's API returns a bare object (not an array) when a collection
contains exactly one element. Standard `System.Text.Json` array deserialization
throws on a bare object. The converter wraps single objects in a list transparently.

---

## `HttpClient.Timeout = TimeSpan.FromMinutes(10)`

**Decision:** Set a 10-minute timeout on the shared `HttpClient` in
`ActionstepApiClient`.

**Why:** The default 100-second timeout is not enough. The first page of
`actiondocuments` with a wide date range can take several minutes while the
server counts all matching records. Silent process exit (no output, no error)
was observed before this was added.

---

## `TFStringBoolConverter` / `TFBoolConverter`

**Decision:** Two separate converters — one for JSON (`System.Text.Json`) and
one for CsvHelper.

**Why:** Actionstep's `isDeleted` field is `"T"` or `"F"` in JSON, and the
manifest CSV also stores booleans as `T`/`F`. Both layers need their own converter
because the serialisation interfaces are different.

---

## Global folder listing without action filter

**Decision:** `GetAllFoldersAsync` calls `GET /api/rest/actionfolders?pageSize=200`
with no `action_eq` filter to retrieve all folders org-wide.

**Why:** The Actionstep API does not document a global folder endpoint, but it
works in practice. This avoids having to enumerate all action IDs first and
make one request per action. If it breaks in a future API version, the fallback
would be to enumerate actions and call the per-action endpoint for each.
