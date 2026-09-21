<div align="center">

# NMS-Vault

**A gallery of No Man's Sky starships, multitools and companions —
downloadable in the format of whichever save editor you use.**

[![License][badge-license]][license]
[![.NET][badge-dotnet]][dotnet]

</div>

---

## What this is

A static gallery, hosted on GitHub Pages, with three rooms:

| Page | Holds |
|---|---|
| **Shipyard** | Starships and frigates |
| **Armoury** | Multitools |
| **Stable** | Companions |

Pick an item, pick your editor, get a file that imports cleanly. Because the site is static, the
conversion runs **in your browser** — a WebAssembly module does the work, and nothing is uploaded
anywhere.

### Supported editors

| Editor | Starship | Multitool | Companion | Frigate |
|---|:---:|:---:|:---:|:---:|
| [NMSE][nmse] | `.nmsship` | `.nmstool` | `.nmspet` | `.nmsfrig` |
| [goatfungus NMSSaveEditor][goatfungus] | `.sh0` | `.wp0` | `.pet` | — |
| [NMS Companion][companion] | `.shp` | `.mlt` | `.cmp` | `.flt` |
| [NomNom][nomnom] | `.shp` | `.mlt` | `.cmp` | `.flt` |

goatfungus has no frigate format, so that download is offered only where it exists.

Freighters are planned — the ship with its tech and cargo, not the base interior.

---

## Project layout

| Project | What it does |
|---|---|
| `src/NmsVault.Json` | The JSON engine — ported from NMSE, adapted for WebAssembly |
| `src/NmsVault.Core` | The vault format, entity payloads, format detection |
| `src/NmsVault.Formats` | One adapter per editor, plus the libNOM bridge |
| `src/NmsVault.Web` | The Blazor WebAssembly site |
| `tools/NmsVault.Ingest` | Console tool for adding items to the gallery |

### Why a custom JSON engine

No Man's Sky saves are JSON, but not ordinary JSON:

- **`1` and `1.0` are different types.** Writing the wrong one corrupts a save.
- **Numbers must survive verbatim.** `0.30000001192092898` has to come back out unchanged, so
  the original text is preserved rather than reformatted.
- **Some strings aren't text.** Byte runs that aren't valid UTF-8 are real binary and must
  round-trip intact.
- **Non-ASCII may not be `\uXXXX`-escaped.** goatfungus's parser only accepts `\u` values ≤ 255,
  so non-ASCII goes out as raw UTF-8 bytes.

`System.Text.Json` and `Newtonsoft.Json` each fail at least two of those, which is why NMSE wrote
its own and why this project ports it rather than starting over.

---

## Building

```bash
dotnet build
dotnet test
```

> **Requires:** [.NET 10 SDK][dotnet]

### Adding to the gallery

Put the export in [data/](data/), under `Starships`, `Multitools` or `Companions`, and build.

Everything about an item lives beside the export, under the same name: a picture as
`<name>.jpg` (further ones numbered from two), and its gallery fields as `<name>.json` —
copy [docs/item-template.json](docs/item-template.json) and fill it in. There is no separate
import step: the web project rebuilds the gallery from `data/` before it builds itself, so a
correction to a backup, a caption or a picture takes effect on the next build.

To rebuild it without building the site:

```bash
dotnet run --project tools/NmsVault.Ingest -- build --from data
```

The gallery it writes under `src/NmsVault.Web/wwwroot/gallery` is generated output and is not
committed. [data/README.md](data/README.md) has the layout and the two fields that are pinned
there rather than derived — `Id`, because two ships are called Rasamama S36, and `DateAdded`,
because a rebuild would otherwise date everything to the moment it ran.

### Publishing

A push to `main` builds, tests and publishes to GitHub Pages; a pull request builds and tests
only. To rehearse the whole of that locally before pushing:

```bash
build-site.cmd
```

One thing it cannot do for itself is re-read the game. The technology icons, class badges,
affinity glyphs and companion tables are extracted from a game install and an NMSE checkout,
neither of which a runner has, and are committed for that reason.

After a game update, first get the icons out. They ship inside the game's archives as
BC7-compressed DDS, which the ingest tool can read neither half of - the archives are Hello
Games' own HGPAK format rather than PSARC, and SkiaSharp cannot decode BC7. So unpack
`TEXTURES/UI/FRONTEND/ICONS` with the PCBANKS Explorer that ships with AMUMSS, then:

```bash
python tools/dds-to-png.py "<unpack-folder>" "<png-folder>"
```

Then the two extractions, which read the PNGs and NMSE's data tables:

```bash
dotnet run --project tools/NmsVault.Ingest -- extract-tech --nmse "<NMSE>/Resources" --icons "<png-folder>"
```

```bash
dotnet run --project tools/NmsVault.Ingest -- extract-pets --nmse "<NMSE>/Resources" --icons "<png-folder>"
```

`extract-pets` writes `gallery/pets.json`: the affinities and their glyphs, which biome and
which species map to which, the 61 battle moves and their names for each affinity, the
personality words, the species table, and the weak/strong matchups. Rerun the ingest afterwards
so the manifest picks up anything that changed - a creature's affinity and personality are
resolved at ingest rather than in the browser.

### Running the gallery

```bash
dotnet run --project src/NmsVault.Web
```

Then <http://localhost:5210>.

> **Restart it after a build.** `dotnet build` and `dotnet test` both rebuild the web project,
> and a running server keeps serving the manifest it started with - which names WebAssembly
> files by a content hash the rebuild has just changed. The page then fails to start with
> *"Expected a JavaScript-or-Wasm module script but the server responded with a MIME type of"*,
> because the file the manifest asks for is no longer there. Stopping and starting the server
> fixes it; if it persists, the build output itself is half-replaced:
>
> ```bash
> rm -rf Build/bin/NmsVault.Web Build/obj/NmsVault.Web && dotnet build src/NmsVault.Web
> ```

---

## Licence

**GNU Affero General Public License v3.0 or later** — see [LICENSE][license].

This project ports code from [NMSE][nmse], which is AGPL-3.0, so it inherits that licence. Ported
files carry headers naming their origin and what changed; [NOTICE.md][notice] has the full
attribution.

Gallery content — item data and images — is **not** software and is licensed separately under
`wwwroot/gallery/LICENSE`.

No Man's Sky is a trademark of Hello Games. This project is unaffiliated with Hello Games, and with
each of the save editors whose formats it reads and writes.

<!-- Links -->
[badge-license]: https://img.shields.io/badge/license-AGPL%203.0-4a8fff
[badge-dotnet]: https://img.shields.io/badge/.NET-10.0-512BD4?logo=dotnet&logoColor=white
[license]: LICENSE
[notice]: NOTICE.md
[dotnet]: https://dotnet.microsoft.com/download/dotnet/10.0
[nmse]: https://github.com/vectorcmdr/NMSE
[goatfungus]: https://github.com/goatfungus/NMSSaveEditor
[companion]: https://www.nexusmods.com/nomanssky/mods/1879
[nomnom]: https://github.com/zencq/NomNom
