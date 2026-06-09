# Deployment

## Machines

| Machine | Role |
|---------|------|
| Parallels VM (macOS host) | Development — write code, build, test |
| Client Windows server | Production — run exports against live Actionstep data |

## Repository

```
https://github.com/seth-inertia-legal/actionstep-export-tool.git
```

Private repo. Both machines clone/pull from `main`.

## Workflow after every code change

**On the Parallels VM (dev machine):**

```bash
cd /path/to/actionstep-export-tool

git add <changed files>
git commit -m "<description>"
git push
```

Always provide exact `git add` paths — never use `git add .` to avoid
accidentally staging gitignored files.

**On the client server:**

```bash
cd C:\Users\IL-Admin\source\repos\actionstep-export-tool
git pull
dotnet run -- export <options>
```

## Per-machine configuration

Each machine has its own `appsettings.Local.json` in `ActionstepDeltaExport/`.
This file is gitignored and contains real credentials and local paths.

Minimum required content:

```json
{
  "Actionstep": {
    "ClientId": "<client ID>",
    "ClientSecret": "<client secret>",
    "Scopes": "openid actiondocuments actionfolders files actions actiontypes"
  },
  "Export": {
    "ManifestPath": "<absolute path to existing manifest CSV, or empty>",
    "OutputRoot":   "<absolute path to output directory>",
    "SinceDate":    "<YYYY-MM-DD or empty to derive from manifest>"
  }
}
```

## Token cache

`tokens.json` is written to the build output directory (`bin/Debug/net8.0/`).
It is gitignored. On first run after `git pull`, or after changing scopes,
delete this file to force re-authentication via the browser.

## .gitignore summary

The following are excluded from the repo:

```
bin/
obj/
tokens.json
appsettings.Local.json
.vs/
*.csv
*.pdf
*.docx
*.xlsx
```

CSV, PDF, and Office files are excluded so export output is never accidentally committed.

## Common deployment issues

### 403 Insufficient scope

A new export feature requires a scope that wasn't in the original OAuth grant.

Fix:
1. Add the missing scope to `Scopes` in `appsettings.Local.json`.
2. Delete `tokens.json`.
3. Run again — the browser will prompt for the new grant.

Scopes needed per feature: see [oauth-setup.md](oauth-setup.md).

### New model files missing on client server

If a build error references a type that exists locally but not on the client,
the new `.cs` files were not included in the last commit.

Fix: on the dev machine, `git add` the missing files, amend or create a new
commit, push, then pull on the client server.

### Build output path for tokens.json

`tokens.json` is written to `AppContext.BaseDirectory`, which is the
`bin/Debug/net8.0/` directory (or `bin/Release/net8.0/` for release builds),
not the project root. Look there if you need to delete it.
