# AGENTS.md

## Push procedure (versioning reminder)

When the user asks to PUSH (or commit+push), before pushing:

1. Ask the user for the next version number (e.g. `v1.0.0`) — the repo has no existing tags yet.
2. Create an annotated tag on the latest commit:
   - `git tag -a vX.Y.Z -m "Release vX.Y.Z"`
   - `git push origin vX.Y.Z`
3. Push the tag BEFORE or together with the code push, so the `.github/workflows/publish.yml` GitHub Action builds and attaches the exe/zip to a new Release.
4. Only bump the version when the user explicitly confirms it — do not invent version numbers.

## Conventions

- Version format: `v` + semantic versioning (e.g. `v1.2.3`).
- The Release is created automatically from tags matching `v*` (see `.github/workflows/publish.yml`).
- Build settings live in `publish_release.bat` and the workflow file; keep them in sync when changed.

## Build

- `dotnet build -c Release` — must succeed with no warnings before committing.
