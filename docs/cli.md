# rangifer

The `rangifer` dotnet tool.

```bash
dotnet tool install --global RoushTech.Rangifer.Cli
```

## `rangifer ef convert`

Convert full migration designers into model deltas.

| Option | Description |
|---|---|
| `--project`, `-p` | Project directory that holds the migrations. Defaults to the current directory. |
| `--context`, `-c` | Only convert migrations whose `[DbContext(typeof(...))]` names this type. |
| `--dry-run` | Check and report the conversion without writing any files. |
| `--accept-hand-edits` | Rebuild every designer as it is now, including lines edited by hand, and rewrite the line hashes. |
