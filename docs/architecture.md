# Architecture

## Tech stack

| Concern | Choice |
|---------|--------|
| Runtime | .NET 8, C# |
| CLI parsing | `System.CommandLine` 2.0.0-beta4.22272.1 |
| Configuration | `Microsoft.Extensions.Configuration` 8.0.0 + JSON provider |
| CSV I/O | `CsvHelper` 33.0.1 |
| HTTP | `System.Net.Http.HttpClient` (built-in) |
| Auth callback | `System.Net.HttpListener` (built-in) |

No database. State lives in flat CSV files on disk.

## Project layout

```
ActionstepDeltaExport/
├── ActionstepDeltaExport.csproj
├── appsettings.json              Placeholder config (committed)
├── appsettings.Local.json        Real credentials (gitignored, per-machine)
├── tokens.json                   OAuth token cache (gitignored, auto-generated)
│
├── Program.cs                    CLI root — registers commands and options,
│                                 applies overrides, calls ExportCommand.RunAsync
│
├── Commands/
│   ├── ExportCommand.cs          Orchestrates document/action/folder export;
│   │                             split into ExportDocumentsAsync,
│   │                             ExportActionsAsync, ExportFoldersAsync
│   └── ProbeCommand.cs           Fetches one raw actiondocument record for
│                                 debugging field name mappings
│
├── Services/
│   ├── AuthService.cs            OAuth 2.0 Authorization Code flow;
│   │                             token cache read/write; silent refresh
│   ├── ActionstepApiClient.cs    All REST calls: document listing, file download,
│   │                             action listing, action type lookup, folder listing
│   │                             (global + per-action), participant name lookup
│   ├── ManifestService.cs        Load/write the documents CSV manifest;
│   │                             GetMaxLastModified for SinceDate derivation
│   └── FolderPathResolver.cs     Builds folderId→path dictionary from flat
│                                 folder list using memoized parent-chain walk
│
├── Models/
│   ├── AppSettings.cs            Config binding: ActionstepSettings + ExportSettings
│   ├── TokenData.cs              OAuth token envelope (access, refresh, expiry,
│   │                             api_endpoint, orgkey)
│   ├── ActionDocument.cs         Actionstep actiondocument DTO + list response;
│   │                             PagingWrapper (actiondocuments, actionfolders,
│   │                             actions); TFStringBoolConverter
│   ├── ActionFolder.cs           Actionstep actionfolder DTO + list response;
│   │                             SingleOrArrayConverter<T>
│   ├── ActionstepAction.cs       Actionstep action/matter DTO + list response;
│   │                             ActionType + ActionTypeResponse
│   ├── Participant.cs            Participant DTO; ResolvedName helper
│   │                             ("LastName, FirstName" or displayName fallback)
│   ├── ManifestRecord.cs         Document manifest CSV row + CsvHelper class map;
│   │                             TFBoolConverter
│   ├── ActionRecord.cs           Actions CSV row + CsvHelper class map
│   └── FolderRecord.cs           Folders CSV row + CsvHelper class map
│
└── Infrastructure/
    └── OAuthCallbackListener.cs  HttpListener wrapper; waits for GET /callback
                                  with ?code= and returns the auth code
```

## Data flow — documents export

```
Program.cs
  └─ ExportCommand.RunAsync(include, download)
       └─ ExportDocumentsAsync
            ├─ ManifestService.Load(manifestPath)     existing records → index
            ├─ ActionstepApiClient.GetDocumentsSinceAsync(sinceDate)
            │    └─ paginated GET /api/rest/actiondocuments?modifiedTimestamp_gteq=...
            ├─ per doc: GetFoldersForActionAsync → FolderPathResolver (lazy cache)
            ├─ per doc: GetParticipantNameAsync (name cache)
            ├─ UpsertRecord → manifestIndex
            └─ ManifestService.Write(documents[_audit]_{ts}.csv)
```

## Data flow — actions export

```
ExportActionsAsync
  ├─ ActionstepApiClient.GetAllActionsAsync()
  │    └─ paginated GET /api/rest/actions?pageSize=200
  ├─ per action: GetActionTypeNameAsync (type name cache)
  └─ WriteCsv<ActionRecord, ActionRecordMap>(actions_{ts}.csv)
```

## Data flow — folders export

```
ExportFoldersAsync
  ├─ ActionstepApiClient.GetAllFoldersAsync()
  │    └─ paginated GET /api/rest/actionfolders?pageSize=200   (no action filter)
  ├─ FolderPathResolver.BuildPathDictionary(allFolders)
  └─ WriteCsv<FolderRecord, FolderRecordMap>(folders_{ts}.csv)
```

## Output files

| File | Produced by |
|------|-------------|
| `documents_audit_{ts}.csv` | `--include documents` without `--download` |
| `documents_{ts}.csv` | `--include documents --download` |
| `actions_{ts}.csv` | `--include actions` |
| `folders_{ts}.csv` | `--include folders` |
| `errors_{ts}.csv` | `--include documents --download` when any file download fails |

Documents CSV columns: `log_id, action_id, document_name, template_id, file_name,
directory, folder_id, file_type, created_by, modified_by, created_date, last_modified,
document_timestamp, is_deleted, folder_path`

Actions CSV columns: `action_id, action_name, action_type_id, file_reference, action_type_name`

Folders CSV columns: `folder_id, action_id, name, parent_folder_id, folder_path`
