# UsenetCurator

An offline pipeline that builds the curated Usenet/NNTP dataset served by VintageHive's `NntpProxy`. It downloads historical newsgroup archives from [Archive.org](https://archive.org/), filters and threads them, injects a generated `alt.hive.help` group, and exports JSON that VintageHive loads at startup.

This is a standalone console app. It is **not** part of `VintageHive.sln` and is run by hand when the bundled newsgroup data needs to be (re)built.

## Pipeline

The run is a seven-phase pipeline (`Program.cs`):

1. **Download and Parse** - Fetch mbox archives per group from Archive.org (`Sources/ArchiveOrgSource.cs`, `CollectionDiscovery.cs`) and parse them into raw articles (`Parsing/MboxParser.cs`, `ArticleParser.cs`, `DateNormalizer.cs`). Downloads are cached under `cache/`.
2. **Filter** - Drop articles outside the configured year range (`Curation/ArticleFilter.cs`).
3. **Number and Cap** - Assign NNTP article numbers and cap each group (`Curation/ArticleNumberer.cs`).
4. **Resolve References** - Rebuild `References` chains so threading works in readers (`Curation/ReferenceResolver.cs`).
5. **Add alt.hive.help** - Inject a generated help newsgroup (`HiveHelpGenerator.cs`).
6. **Deduplicate** - Remove articles with duplicate `Message-ID`s across all groups.
7. **Export** - Write the dataset as JSON (`Export/JsonExporter.cs`).

## Running

```bash
cd tools/UsenetCurator
dotnet run -- [options]
```

### Modes

- **Full mode** (default) - Discovers groups from Archive.org's Usenet collections (or the groups you name) and writes to `data/usenet`. This is the large, exhaustive build.
- **CI / bundle mode** (`-ci`, `--ci`, or `--bundle`) - Uses the curated set in `GroupManifest.cs`, applies per-group caps, and writes to `../../Statics/usenet` so the data is embedded into the VintageHive build.

### Options

| Option | Description | Default |
|--------|-------------|---------|
| `-ci` / `--ci` / `--bundle` | Run in CI/bundle mode (manifest groups -> `Statics/usenet`) | off (full mode) |
| `--output <dir>` | Output directory | `data/usenet` (full), `../../Statics/usenet` (CI) |
| `--years <min>-<max>` | Year range to keep | `1980-2005` |
| `--max-per-group <n>` | Cap articles per group | manifest value (CI) / no cap (full) |
| `--groups <a,b,c>` | Only these newsgroups | all discovered / all manifest groups |
| `--collections <a,b,c>` | Only these Archive.org collections (the `usenet-` prefix is added automatically) | all known collections |

Press `Ctrl+C` to cancel; the current phase stops cleanly.

### Examples

```bash
# Rebuild the bundled dataset that ships with VintageHive
dotnet run -- --bundle

# Full pull of a few groups for a narrow year range
dotnet run -- --groups comp.sys.ibm.pc.games.action,rec.games.video --years 1994-1999

# Full discovery limited to the comp.* and rec.* collections
dotnet run -- --collections comp,rec --max-per-group 2000
```

## How the data reaches VintageHive

VintageHive's `Proxy/Usenet/UsenetDataSource.cs` loads the dataset on startup:

- **Bundled** - `Statics/usenet/usenet.groups.json` and `usenet.articles.json.gz` are compiled in as embedded resources. Rebuild them with `--bundle`, then rebuild VintageHive.
- **External** - Files under `data/usenet` (the full-mode default) are read at runtime if present.

After a full-mode run, restart VintageHive to pick up the new data.
