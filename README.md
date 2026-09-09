# MigDb

I started this because I needed to migrate some databases, and commercial tools are too expensive

Structural change flows through standard forward-only **migrations** using timestamped directories. These should contain `.sql` scripts, applied once and journalled in the target database.

Stored procedures, views, functions and triggers flow through the **programmable** pipeline, and deployments are gated using hashing. Only programmables whose hash changes get deployed.

Both halves lean on Microsoft's own SQL tooling rather than a hand-rolled parser.

**DacFx** does the model-level work. The schema project is a `.sqlproj` built into a dacpac, and
`SchemaComparison` diffs dacpac against dacpac to derive a migration from two git revisions, or
dacpac against a live database to report drift. `schema deploy` publishes a dacpac to the target
directly through `DacServices`.

**ScriptDom** does the script-level work. A `TSql160Parser` and a couple of `TSqlFragmentVisitor`
implementations work out which object each statement targets, so a generated delta can be split into
one script per object and the boilerplate DacFx emits variable declarations, `PRINT`s, `SET`s 
stripped back to reviewable T-SQL. It is also what rewrites a programmable's `CREATE` into
`CREATE OR ALTER` on the way to the database.

## Projects

| Project | What |
| --- | --- |
| `MigDb.CLI` | Spectre.Console.Cli front end — builds to `MigDb.exe` |
| `MigDb.Core` | Migrations, schema, repositories, DacFx/ScriptDom |
| `MigDb.Test` | xUnit v3 tests over Core |
| `MigDb.VSExtension` | VS tool window that shells out to the CLI |

## Requirements

- .NET 10 SDK
- git (must be on `PATH`)
- SQL Server or Azure SQL

Git is used to read the schema project at past revisions. This allows reverts and generating local changes without depending on a database

## Build

```powershell
dotnet build MigDb.slnx -c Release
dotnet publish MigDb.CLI/MigDb.CLI.csproj -c Release -r win-x64 --self-contained false -o out
```

`MigDb.exe` must be on path for visual studio extension to work. We search path for the tool.

## Quick start

```powershell
MigDb config set Migration.SchemaProjectPath C:\src\MyProduct\Database
MigDb project create                                  # scaffold Migrations/ and Schema/
MigDb schema journal init -c "<CONNECTION>"           # create the migdb journal schema

MigDb migration create                                # new timestamped directory
MigDb migration prepare  20260901_101500              # number files, write manifest
MigDb migration validate 20260901_101500
MigDb migration apply --pending -c "<CONNECTION>" --dry-run
MigDb migration apply --pending -c "<CONNECTION>"
```

## Project structure

`project create` scaffolds this under `Migration.SchemaProjectPath`:

```
<SchemaProjectPath>/
  Migrations/
    Common/          shared migrations
    Projects/        per-project migration roots, one folder per project
  Schema/
    Common/          the shared .sqlproj and its object folders
    Projects/        per-project .sqlproj folders
```

Each migration is a timestamped directory of numbered `.sql` files plus the manifest that locks
their content:

```
Migrations/Common/20260901_101500
  01_Tables.sql
  02_Data.sql
  manifest.json
```

`Common` applies everywhere; `Projects/<name>` applies only to that project. Commands select
between them with `-t|--type common|project` and `--project <NAME>`, defaulting to `common`.

## Commands

`config` — read and write the config file

| Command | What |
| --- | --- |
| `config get [<KEY>]` | Show values as stored and as they resolve; `--path` lists the file precedence |
| `config set <KEY> <VALUE>` | Write to the user config, or the application config with `-g\|--global` |

`project` — project utilities

| Command | What |
| --- | --- |
| `project create [<PATH>]` | Create the migration and schema directory structure |

`migration` — migration utilities

| Command | What |
| --- | --- |
| `migration create [<NAME>]` | Create a timestamped migration directory |
| `migration show <NAME>` | Reveal the directory in Explorer |
| `migration list` | List available migration directories |
| `migration hash <NAME-OR-PATH>` | Hash a migration directory, or a single `.sql` file |
| `migration prepare <NAME-OR-PATH>` | Number the files and write the manifest |
| `migration validate <NAME-OR-PATH>` | Validate a directory against its manifest |
| `migration apply [<NAME-OR-PATH>]` | Apply to the target database, by name/path, `--pending`/`--tail`, or `--from`/`--to` |
| `migration generate <NAME-OR-PATH>` | Generate delta scripts by comparing two revisions of the schema project |
| `migration revert -o <PATH>` | Generate the scripts that undo the last commit; `--force` runs them and winds the journal back |

`schema` — schema utilities

| Command | What |
| --- | --- |
| `schema build` | Build the schema project and report any build errors |
| `schema deploy -c <CONNECTION>` | Deploy the schema project to the target database |
| `schema drift -c <CONNECTION>` | Compare the project against a database and report objects the two disagree on |
| `schema baseline -c <CONNECTION>` | Record current programmable hashes without executing them |
| `schema journal init -c <CONNECTION>` | Create the journal schema and tables (idempotent) |
| `schema journal status -c <CONNECTION>` | Check whether the journal resources exist |
| `schema journal reset -c <CONNECTION>` | Drop the journal schema, application tables untouched |

`db` — read the journal in a target database

| Command | What |
| --- | --- |
| `db migration list -c <CONNECTION>` | List migrations recorded in the database |
| `db revert list -c <CONNECTION>` | List what reverts have taken out of the journal |

`logs` — reveal the log directory

Global options: `--quiet`, `--verbose`, `--debug` control console output, and `--projectPath <PATH>`
overrides `Migration.SchemaProjectPath` for one invocation. Anything that touches a database takes
`-c|--connection`, and the destructive commands take `--dry-run` to rehearse.
