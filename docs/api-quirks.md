# Actionstep API Quirks

Documented gotchas discovered during development. Read this before touching any
API call code.

## Base URL

The API endpoint is **not fixed** — it is returned in the OAuth token response
as `api_endpoint`, e.g. `https://ap-southeast-2.actionstep.com/api/`. All
resource paths are relative to this. `ActionstepApiClient` stores it as `_base`
and sets it as `HttpClient.BaseAddress`.

Optional override: set `Actionstep.ApiEndpointOverride` in `appsettings.Local.json`
to force a specific regional endpoint.

## Paging

Paging metadata is nested under the resource name, not at a top level:

```json
{
  "meta": {
    "paging": {
      "actiondocuments": {
        "recordCount": 4821,
        "pageCount": 25,
        "page": 1,
        "pageSize": 200
      }
    }
  }
}
```

The `PagingWrapper` class has one nullable property per resource type
(`ActionDocuments`, `ActionFolders`, `Actions`). Add a new property there
whenever adding a new paginated endpoint.

**Maximum page size is 200.** Using larger values does not error but is ignored.

## Boolean fields

Actionstep sends booleans as the **strings `"T"` or `"F"`**, not JSON `true`/`false`.

- `TFStringBoolConverter` (in `ActionDocument.cs`) handles this for JSON deserialization
- `TFBoolConverter` (in `ManifestRecord.cs`) handles this for CsvHelper CSV round-trips

## Single-object vs array responses

When a collection endpoint returns exactly one result, Actionstep returns a
**bare JSON object** instead of a one-element array. Use `SingleOrArrayConverter<T>`
(defined in `ActionFolder.cs`) on any `List<T>` property that can exhibit this.

Currently applied to `ActionFolderListResponse.Folders`.

## Date filter format

The `modifiedTimestamp_gteq` query parameter must be formatted as:

```
yyyy-MM-ddTHH:mm:ss
```

**Without** a timezone offset or `Z` suffix. The API interprets it as UTC.
Do not URI-encode the `T` separator — use `Uri.EscapeDataString` only on the
full parameter value.

## File downloads

```
GET /api/rest/files/{fileIdentifier}?part_number=N
```

- Files are downloaded in **5 MB chunks**. The number of chunks is
  `ceil(fileSize / (5 * 1024 * 1024))`.
- `fileIdentifier` contains characters like `::` and `;` that are valid in URL
  path segments and **must not be percent-encoded**. Pass the identifier verbatim
  in the URL string — do not call `Uri.EscapeDataString` on it.
- The API field name on `actiondocuments` is `"file"`, not `"fileIdentifier"`.
  The C# property is named `FileIdentifier` for clarity; the `[JsonPropertyName]`
  attribute maps it correctly.

## HTTP timeout

The default `HttpClient` timeout (100 seconds) is **not enough** for large datasets.
The first page of `actiondocuments` with a wide date range can take several minutes
while the server counts all matching records. Always set:

```csharp
_http.Timeout = TimeSpan.FromMinutes(10);
```

## created_by and modified_by

- `createdBy` is a **participant ID** in `links.createdBy` — it must be resolved
  to a display name via `GET /api/rest/participants/{id}`.
- There is **no `modifiedBy` field** on the `actiondocuments` endpoint.
  The tool uses `checkedOutTo` (a direct string field on the document) as a
  proxy for `modified_by`, falling back to `createdBy` if null.

## Participant display name

`GET /api/rest/participants/{id}` returns a `participants` object with
`displayName`, `firstName`, `lastName`. The `ResolvedName` helper property
on `Participant` returns `"LastName, FirstName"` if both are present,
falling back to `displayName`.

## Action type names

Action types are not embedded in the `actions` response — they must be fetched
separately:

```
GET /api/rest/actiontypes/{id}
```

Results are cached in `_actionTypeCache` for the lifetime of the `ActionstepApiClient`
instance to avoid repeated lookups.

## Global folder listing

`GET /api/rest/actionfolders` without an `action_eq` filter returns **all folders**
across the entire org. This is undocumented but works. Used by `GetAllFoldersAsync`
for the `--include folders` full dump.

Per-action folder listings use `?action_eq={actionId}` and are used by
`GetFoldersForActionAsync` for the per-document folder path resolution.

## Required OAuth scopes

See [oauth-setup.md](oauth-setup.md) for the full scope list. A 403 with
`"insufficient_scope"` always means a scope is missing — add it to
`appsettings.Local.json` and delete `tokens.json` to re-authenticate.
