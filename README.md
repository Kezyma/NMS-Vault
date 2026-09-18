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
