# Actionstep Export Tool — Claude Context Index

This file is the entry point for AI-assisted sessions on this codebase.
Read this first, then load whichever detail doc is relevant to the task.

## What this project is

A .NET 8 CLI tool that exports documents, actions (matters), and folders from the
Actionstep legal practice management platform via its REST API. Built for the
Peyton Bolin client migration. Outputs CSV manifests and optionally downloads files.

## Repo location

`https://github.com/seth-inertia-legal/actionstep-export-tool.git`

## Detail documents

| File | Load when… |
|------|-----------|
| [docs/architecture.md](docs/architecture.md) | Understanding project structure, file layout, or tech stack |
| [docs/cli-reference.md](docs/cli-reference.md) | Working on CLI commands, options, or usage examples |
| [docs/api-quirks.md](docs/api-quirks.md) | Touching any Actionstep API calls, JSON parsing, or paging |
| [docs/oauth-setup.md](docs/oauth-setup.md) | Working on auth, scopes, token caching, or credential config |
| [docs/decisions.md](docs/decisions.md) | Understanding why something was built the way it was |
| [docs/deployment.md](docs/deployment.md) | Deploying changes from the dev VM to the client server |

## Critical rules (always apply)

- `appsettings.Local.json` — holds real credentials, **never commit**
- `tokens.json` — OAuth token cache, **never commit**
- `appsettings.json` in repo contains only placeholder values (`YOUR_CLIENT_ID` / `YOUR_CLIENT_SECRET`)
- After every code change, provide exact `git add / commit / push` commands

## Quick orientation

```
ActionstepDeltaExport/
  Program.cs                  CLI wiring (System.CommandLine)
  Commands/ExportCommand.cs   Core export logic (documents, actions, folders)
  Services/
    AuthService.cs            OAuth 2.0 browser flow + token cache
    ActionstepApiClient.cs    All API calls, paging, file download
    ManifestService.cs        CSV read/write for document manifest
    FolderPathResolver.cs     Reconstructs folder paths from flat folder list
  Models/                     DTOs, CSV maps, AppSettings
  Infrastructure/
    OAuthCallbackListener.cs  Local HttpListener for OAuth callback
```
