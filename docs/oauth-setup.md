# OAuth Setup

## Flow

The tool uses **OAuth 2.0 Authorization Code** flow.

1. On first run, `AuthService` opens the system browser to Actionstep's authorize URL.
2. A local `HttpListener` on a random port waits for the redirect callback.
3. The auth code is exchanged for tokens at the token endpoint.
4. Tokens are saved to `tokens.json` next to the executable.
5. On subsequent runs, the access token is reused. If it is within 60 seconds of
   expiry, a silent refresh is attempted using the refresh token.
6. If the refresh fails, the browser flow runs again.

## Credential file — appsettings.Local.json

Create this file in `ActionstepDeltaExport/` on each machine. It is gitignored
and **must never be committed**.

```json
{
  "Actionstep": {
    "ClientId": "<your OAuth client ID>",
    "ClientSecret": "<your OAuth client secret>",
    "Scopes": "openid actiondocuments actionfolders files actions actiontypes"
  },
  "Export": {
    "ManifestPath": "C:\\path\\to\\existing_manifest.csv",
    "OutputRoot":   "C:\\path\\to\\export\\output",
    "SinceDate":    "2026-01-01"
  }
}
```

Values in `appsettings.Local.json` **override** matching values in `appsettings.json`.
Fields not present in the Local file are taken from `appsettings.json`.

## Required OAuth scopes

All scopes must be space-separated in the `Scopes` field:

| Scope | Required for |
|-------|-------------|
| `openid` | All runs (identity) |
| `actiondocuments` | `--include documents` |
| `actionfolders` | Document folder path resolution; `--include folders` |
| `files` | `--include documents --download` (file download) |
| `actions` | `--include actions` |
| `actiontypes` | `--include actions` (action type name lookup) |

For a full export run all scopes are needed:
```
openid actiondocuments actionfolders files actions actiontypes
```

## Changing scopes

Scopes are baked into the OAuth grant at the time of login. To add a scope:

1. Add it to `Scopes` in `appsettings.Local.json`.
2. Delete `tokens.json` (next to the executable, in `bin/Debug/net8.0/`).
3. Run again — the browser flow will prompt for the new scope.

## Token cache file

`tokens.json` is written to `AppContext.BaseDirectory` (the `bin/Debug/net8.0/`
output directory). It stores the access token, refresh token, expiry, org key,
and API endpoint. It is gitignored.

## CallbackPort

Defaults to `0` (random free port). Set `Actionstep.CallbackPort` in
`appsettings.Local.json` to a specific port if needed for firewall rules.

## ApiEndpointOverride

If the API endpoint returned by the token response is incorrect for your region,
set `Actionstep.ApiEndpointOverride` to override it:

```json
"ApiEndpointOverride": "https://us-west-2.actionstep.com/api/"
```
