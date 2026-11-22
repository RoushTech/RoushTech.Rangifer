# Working in RoushTech.Rangifer

## Git

**Never `git commit`, `git push`, `git tag`, or otherwise mutate git state on William's behalf.** He drives the git operations himself. Prepare the working tree (edit files, run builds/tests, verify things pack cleanly) and hand off. Do not offer to commit or push, do not ask for permission to commit or push, and do not run those commands even if the flow "seems ready." He'll take it from there.

This includes:
- `git commit` (and `--amend`)
- `git push` (regular, `--force`, `--force-with-lease`)
- `git tag`
- Moving/deleting tags on the remote
- Anything that would fire the publish workflow (every push to master)

Local, non-git operations that only affect the working tree (Edit/Write/dotnet build/dotnet pack against `./artifacts`) are fine.

Commit messages: under 80 characters, a good brief summary.

## Releasing

Every package shares the version in `Directory.Build.props` (`VersionPrefix`, plus `VersionSuffix` for prereleases). Every push to master publishes all packages if the `v<version>` tag doesn't exist yet, then creates that tag and a GitHub release.

## Writing

No em dashes or en dashes anywhere: code, comments, docs, commit messages.

## Commands

```bash
dotnet build RoushTech.Rangifer.slnx
dotnet test RoushTech.Rangifer.slnx
dotnet pack RoushTech.Rangifer.slnx --configuration Release --output ./artifacts
```

## EF Core packages

- The runtime and Design packages share `BlockSequence` through `InternalsVisibleTo`, so both sides replay a delta chain with the same code. Ship them at the same version.
- Do not use EF Core APIs that raise EF1001 (internal namespaces, dependency properties like `OperationReporter`). Warnings are errors here and are never suppressed.
- Never publish code from client projects. Tests generate their own designers from `Tests/Models/VersionedContext.cs`.
