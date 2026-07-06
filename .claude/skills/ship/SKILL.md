---
name: ship
description: Release the current adrilight changes — run tests, bump the version, update README/WhatsNew.xaml/SESSIONS.md, commit + push, publish a local build, and optionally zip + create the GitHub release. Use when the user says "ship it", "release", "publish this", or when a fix/feature session is complete and needs to reach the repo and the running app.
---

# /ship — adrilight release procedure

Runs the complete adrilight release ritual. Every step is mandatory unless marked optional.
Work from the repo root (`c:\DevProvects\adrilight`). Do the steps **in order** — later steps
depend on earlier ones (e.g. publish must come after the version bump, or the exe is stamped
with the old version).

## Arguments

- `/ship` — commit + push + publish (local ship; no GitHub release)
- `/ship release` — everything, including zip + GitHub release
- `/ship X.Y.Z` — override the auto-chosen version number (with or without `release`)

## Step 0 — Preconditions

1. `git status --short` — there must be uncommitted changes to ship (or, if the tree is clean
   but the last commits are unpushed/unreleased, confirm with the user what to ship).
2. Decide the new version:
   - Read the current version from `adrilight/Properties/AssemblyInfo.cs` (`AssemblyVersion`).
   - Bug fixes → bump patch (3.7.5 → 3.7.6). New feature → bump minor (3.7.5 → 3.8.0).
   - If the working tree already contains a version bump (AssemblyInfo differs from the last
     commit), use that version — do not bump twice.
3. Write a one-paragraph changelog draft (what changed, root cause if a fix) and confirm the
   version + changelog with the user before proceeding. This single draft feeds all three doc
   updates in Step 3 — write it once, reuse it.

## Step 1 — Tests

```
dotnet test adrilight.Tests/adrilight.Tests.csproj
```

All tests must pass. If any fail, **stop** and report — do not release on red.
Note the total test count for the SESSIONS.md entry.

## Step 2 — Version bump

Edit `adrilight/Properties/AssemblyInfo.cs`: set both `AssemblyVersion` and
`AssemblyFileVersion` to the new version. These two must always match.

## Step 3 — Documentation (three files, same content, different shapes)

All three derive from the Step 0 changelog draft:

1. **SESSIONS.md** (repo root): insert a new entry at the **top** of the entry list (directly
   after the intro paragraphs), format:
   ```
   ### YYYY-MM-DD — Short title (vX.Y.Z)
   1. **Root cause:** … (for fixes) / bullet list of what was added (for features)
   2. **Fix:** …
   3. Tests: N new / none — reason. Total: N/N.
   4. Version bumped to X.Y.Z.
   ```
2. **README.md**: add a `### What's new in X.Y.Z` subsection at the top of the What's-new
   area (newest first). User-facing wording — describe behaviour, not internals.
3. **adrilight/View/WhatsNew.xaml**: add a new version section at the **top** of the existing
   sections (newest first). Copy the XAML structure of the most recent existing section
   exactly (same styles/margins); change only the version heading and bullet text.
   User-facing wording, same content as the README entry.

CLAUDE.md is **not** routinely updated — only touch it if the architecture, test counts table,
or a durable gotcha changed.

## Step 4 — Commit and push

- Commit message style (match repo history): `Fix: <summary> (vX.Y.Z)` or
  `Feature: <summary> (vX.Y.Z)` or `Bump version to X.Y.Z — <summary>`.
- End the message with the Co-Authored-By line required by the harness.
- `git push` to `origin main`.

## Step 5 — Publish local build

1. If `adrilight` is running it locks the published DLLs. Check with
   `Get-Process adrilight -ErrorAction SilentlyContinue`. If running, tell the user and stop
   it (`Stop-Process -Name adrilight`) before publishing — note that it was running so Step 7
   offers a restart.
2. ```
   dotnet publish adrilight/adrilight.csproj -c Release --self-contained false -o ./publish/adrilight-X.Y.Z
   ```
3. Verify the version stamp:
   `(Get-Item ./publish/adrilight-X.Y.Z/adrilight.exe).VersionInfo.FileVersion` — must equal
   X.Y.Z. If it doesn't, the bump in Step 2 didn't take; fix and republish.

## Step 6 — GitHub release (only with `release` argument)

1. Copy the Arduino sketch into the publish folder, **preserving the subfolder path**:
   `Arduino/adrilight/adrilight.ino` → `publish/adrilight-X.Y.Z/Arduino/adrilight/adrilight.ino`.
   **Never skip this** — end users cannot use the app without the sketch to flash their Arduino.
2. Zip the whole folder: `Compress-Archive -Path ./publish/adrilight-X.Y.Z -DestinationPath ./publish/adrilight-X.Y.Z.zip`.
3. Create the release with the `gh` CLI, authenticated via the `GITHUB_RELEASE_TOKEN`
   environment variable:
   ```
   $env:GH_TOKEN = $env:GITHUB_RELEASE_TOKEN
   gh release create vX.Y.Z ./publish/adrilight-X.Y.Z.zip --title "adrilight X.Y.Z" --notes "<changelog>"
   ```
   Release notes = the README What's-new entry text.

## Step 7 — Restart

If adrilight was running before Step 5 (or the user asks), start the fresh build:
`Start-Process ./publish/adrilight-X.Y.Z/adrilight.exe`. Remind the user it starts minimized
to the system tray.

## Final report

State plainly: version shipped, test count, commit hash, pushed or not, publish path,
release URL (if created), and whether adrilight was restarted.
