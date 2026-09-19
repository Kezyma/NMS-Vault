# Format coverage and conversion loss

What each editor's export format carries, and therefore what is lost converting between
them. This is reference material for the adapters and for the warnings the gallery shows
before a download.

## How to read this

| Mark | Meaning |
|---|---|
| ✅ | Carried |
| ❌ | Not carried — lost in conversion |
| ⚠️ | Carried partially, see the note |
| 🔄 | Derived: absent from the file, but recomputable from the payload |
| — | Kind not supported by that editor at all |

**Confidence.** The NMSE columns are verified against 23 real exports in
`tests/fixtures/nmse`. The goatfungus, NMS Companion (Kaii) and NomNom columns are read
from [libNOM.collect](https://github.com/zencq/libNOM.collect)'s writers — a good proxy for
NomNom (same author) and a reasonable one for Kaii, but **not confirmed against the editors
themselves**, because as of 7.03 Cosmos none of the three can load a current save. Anything
marked *inferred* is weaker still.

---

## Key space

Not a field, but it governs everything else.

| | NMSE | goatfungus | Kaii | NomNom |
|---|---|---|---|---|
| Keys on disk | Human-readable | Human-readable | **Obfuscated** | **Obfuscated** |

`ExportGoatfungus()` calls `Deobfuscate()`; `ExportKaii()` and `ExportStandard()` call
`Obfuscate()`. NMSE serialises with `skipReverseMapping: true`, so always readable.

This is why the mapping table's currency matters: `ToKey` passes unknown names through
unchanged, so obfuscating with a stale table produces a *mixed* key space rather than an
error. NMS-Vault uses NMSE's table (`libMBIN_version 7.1.0.1`, current for 7.03).

---

## Starship

| Field | Lives in the save at | NMSE | goatfungus | Kaii | NomNom |
|---|---|---|---|---|---|
| Ship object | `PlayerStateData.ShipOwnership[i]` | ✅ | ✅ | ✅ | ✅ |
| Legacy colour flag | `PlayerStateData.ShipUsesLegacyColours[i]` | ✅ `UsesLegacyColours` | ❌ | ✅ `Ship.4hl` | ✅ `Data.UseLegacyColours` |
| Colours | `CharacterCustomisationData[n].CustomData.Colours` | ✅ | ❌ | ✅ | ✅ |
| **Parts** (`DescriptorGroups`) | `…CustomData.DescriptorGroups` | ✅ | ❌ | ❌ | ❌ |
| **Texture options** | `…CustomData.TextureOptions` | ✅ | ❌ | ❌ | ❌ |
| **Palette ID** | `…CustomData.PaletteID` | ✅ | ❌ | ❌ | ❌ |
| Bone scales, Scale | `…CustomData.BoneScales`, `.Scale` | ✅ | ❌ | ❌ | ❌ |
| Selected preset | `CharacterCustomisationData[n].SelectedPreset` | ✅ | ❌ | ❌ | ❌ |
| Corvette base | `PlayerStateData.PersistentPlayerBases[]` | ✅ `Base` | ❌ | ❌ | ✅ `Data.PersistentPlayerBases` |
| Description | — (editor metadata) | ❌ | ❌ | ✅ | ✅ |
| Thumbnails | — | ❌ | ❌ | ✅ ×6 | ✅ ×6 `Preview` |
| Date created | — | ❌ | ❌ | ❌ | ✅ |
| Starred | — | ❌ | ❌ | ❌ | ✅ |

### The significant one: customisation is not just colours

NMSE carries the **whole** `CharacterCustomisationData` entry. Kaii and NomNom carry only
the colours array — verified from libNOM's own JSONPath,
`CharacterCustomisationData[{i}].CustomData.Colours`, which terminates at `.Colours`.

That is not an edge case. Of the 17 ships in the fixture corpus, **8 carry customisation,
and every one of those has `DescriptorGroups = 3` and `TextureOptions = 1` alongside its 3
colours.** They include Starborn Phoenix, Iron Vulture, The Wraith and Vintage Interceptor —
the visually distinctive ships people would actually want from a gallery. Iron Vulture also
carries `PaletteID = "^SHIP_METALLIC"`.

So converting a customised ship to Kaii or NomNom yields **the right model in the right
colours with the wrong parts**. The gallery should warn before that download rather than
let someone discover it in-game.

goatfungus loses customisation entirely, colours included.

---

## Multitool

| Field | NMSE | goatfungus | Kaii | NomNom |
|---|---|---|---|---|
| Multitool object | ✅ | ✅ | ✅ `MultiTool` | ✅ `Data.Multitool` |
| Legacy colour flag | ✅ | ✅ | ✅ | ✅ |
| Customisation | ✅ | ✅ | ✅ | ✅ |
| Type | 🔄 | ❌ | ⚠️ inferred | ✅ `Data.Type` |
| Description | ❌ | ❌ | ✅ | ✅ |
| Thumbnails | ❌ | ❌ | ✅ ×6 | ✅ ×6 |

Multitools fare far better than ships, for one structural reason: **they store both their
legacy-colour flag and their customisation *inline* on the object itself** —
`UseLegacyColours` (no trailing *s*) and `CustomisationData` are keys on the multitool, not
parallel arrays elsewhere in the save. Anything that copies the whole object gets them free.
That is exactly why multitool export never suffered the bug ship export did.

**Type** is not a game field — no fixture has one. It is editor metadata (Pistol / Rifle /
Staff / Alien), derivable from `Resource.Filename` and, for the four types that share
`MULTITOOL.SCENE.MBIN`, from which per-class stat range the tool rolled within. So it is
marked 🔄 for NMSE rather than ❌. libNOM reads goatfungus's `Type` as `null` and drops it on write, so a
goatfungus round trip loses it even where the source had it.

---

## Companion

| Field | NMSE | goatfungus | Kaii | NomNom |
|---|---|---|---|---|
| Pet object | ✅ | ✅ | ✅ `Companion` | ✅ `Data.Pet` |
| Accessories | ✅ | ❌ | ✅ `Accessories` | ✅ `Data.AccessoryCustomisation` |
| Galactic address | 🔄 | ❌ | ✅ | ❌ |
| Galaxy | 🔄 | ❌ | ✅ | ❌ |
| Glyphs string | 🔄 | ❌ | ✅ | ❌ |
| Description | ❌ | ❌ | ✅ | ✅ |
| Thumbnails | ❌ | ❌ | ✅ | ✅ |

**Accessories nest three different ways**, which is the main trap here:

| Where | Shape |
|---|---|
| The save | `PetAccessoryCustomisation[i]` = `{ Data: [slot0, slot1, slot2] }` |
| NMSE `.nmspet` | the **flat array**, spliced onto the pet as `PetAccessoryCustomisation` |
| NomNom `.cmp` | the wrapper object, as a sibling at `Data.AccessoryCustomisation` |
| Kaii `.cmp` | the wrapper object, at root as `Accessories` |

NMS-Vault stores the **innermost flat array**, so each adapter re-wraps rather than
unwraps. goatfungus carries the pet only — accessories are lost.

Kaii's galactic address, galaxy and glyph string are computed from the pet's voxel fields,
so they are derived rather than authored: absent elsewhere, but reconstructible.

---

## Frigate

| Field | NMSE | goatfungus | Kaii | NomNom |
|---|---|---|---|---|
| Frigate object | ✅ | — | ✅ | ✅ `Data.Frigate` |
| Description / thumbnails | ❌ | — | ✅ | ✅ |

**goatfungus has no frigate format at all** — confirmed in libNOM's `SUPPORTED_FORMATS`,
which lists only Kaii and Standard. An unsupported combination throws, so the gallery must
disable that download rather than attempt it.

---

## Freighter *(deferred)*

Scoped as the ship with its tech and cargo, not the base interior. Recorded now because the
gaps shape whether it can ship later.

| Field | NMSE | goatfungus | Kaii | NomNom |
|---|---|---|---|---|
| Freighter object, home system, name | ❌ | — | ✅ | ✅ |
| `Inventory`, `Inventory_TechOnly` | ⚠️ separate files | — | ✅ | ✅ |
| **`Inventory_Cargo`** | ⚠️ separate file | — | ❌ | ✅ |
| Colours | ❌ | — | ❌ | ✅ |

Two blockers, both noted in the plan:

- **NMSE has no whole-freighter-ship format.** `.nmsfreight` is the base interior — a
  `PersistentPlayerBases` entry — and `.nmsfc` / `.nmsft` are bare cargo and tech
  inventories. Nothing carries the freighter itself.
- **Kaii drops freighter cargo and colours.** Its writer emits five keys and nulls
  `Inventory_Cargo` and `Colours` on read. That removes precisely the cargo a freighter
  backup is for.

---

## Gallery-only fields

Held in the vault's `Vault` block and carried by no editor format. Dropped on every
outbound conversion, by design.

| Field | Notes |
|---|---|
| `Id` | Permalink slug |
| `AlternativeNames` | Search only |
| `Tags` | Search only |
| `Author`, `DateAdded`, `GameVersion` | Provenance |
| `SchemaVersion` | Vault format version |

`DisplayName`, `Description` and `Images` are the exceptions — they map onto Kaii's and
NomNom's description and thumbnail slots.

---

## Conversion loss at a glance

Reading down: what is lost taking an item **from** the row format **to** the column format.

| from ↓ to → | NMSE | goatfungus | Kaii | NomNom |
|---|---|---|---|---|
| **NMSE** | — | legacy flag, all customisation, base | parts, texture, palette, preset, base | parts, texture, palette, preset |
| **goatfungus** | nothing (it carries least) | — | nothing | nothing |
| **Kaii** | description, thumbnails | legacy flag, colours, description, thumbnails | — | date, starred |
| **NomNom** | description, thumbnails, date, starred | legacy flag, colours, metadata, base | date, starred | — |

Two asymmetries worth internalising:

1. **goatfungus is the floor.** Converting *to* it loses the most; converting *from* it
   loses nothing, because it carried nothing extra to begin with.
2. **NMSE is the ceiling for game data, the floor for presentation.** It carries the
   richest payload — full customisation, corvette bases — and none of the description or
   thumbnail metadata the other two have.

Because the vault stores NMSE's shape plus its own metadata block, it is a superset of all
four: nothing that arrives is lost in storage, only on the way back out.

---

## Verbatim writer structures

Quoted from libNOM.collect, so the adapters can be checked against the source they were
derived from.

**Kaii** (`Starship.ExportKaii`) — readable envelope, hardcoded obfuscated keys inside `Ship`:

```
{ "Ship": { "@Cs": <ship>, "4hl": <legacy colour flag> },
  "Colours": <colours array>,
  "Description": <string>,
  "FileVersion": 1,
  "Thumbnail": <base64|null>, "Thumbnail2" … "Thumbnail6": <base64|null> }
```

**NomNom** (`CollectionItem.ExportStandard`) — readable envelope and readable `Data`
sub-keys, obfuscated values:

```
{ "Data": { … },
  "DateCreated": <UTC>,
  "Description": <string>,
  "FileVersion": 2,
  "Preview": <base64|null>, "Preview2" … "Preview6": <base64|null>,
  "Starred": <bool> }
```

`Obfuscate()` runs before the envelope is built in both, which is why envelope keys stay
readable while payload keys do not. Note there is **no `Thumbnail1` or `Preview1`** — the
first slot is unnumbered.

---

## Our mapping table covers Cosmos

The gap that made libNOM unusable is worth being able to prove absent here, so
`KeyObfuscator.CountUnmapped` walks a payload and counts keys the table has no obfuscated
form for. Tested against real 7.03 ship and multitool payloads including Iron Vulture,
Vintage Interceptor and Starborn Phoenix: **zero unmapped keys**.

The only unmapped names anywhere in a vault document are `Ship`, `Base` and `Vault` — export
scaffolding rather than game keys, correctly absent from the table. That is asserted as a
test so nobody "fixes" it by adding them.

This will go stale. `mapping.json` needs refreshing from MBINCompiler each game update; the
counter is what turns that from a silent corruption into a failing test.

---

## Open questions

1. **`Thumbnail` vs `Thumbnail1`** — *resolved as far as libNOM goes*: its writer emits
   `Thumbnail` then `Thumbnail2`–`Thumbnail6`, quoted above. Still unconfirmed against the
   real NMS Companion.
2. **Whether Kaii's ship `Colours` is really only the array** — verified from libNOM's
   JSONPath (`CharacterCustomisationData[i].CustomData.Colours`, terminating at `.Colours`),
   but not from a real file.
3. **Multitool `Type` vocabulary** — NMS-Vault derives Staff / Alien / Royal / Rifle /
   Pistol from the model path and the stat ranges. The exact strings NomNom expects are
   unconfirmed, and the two vocabularies are known to differ: what the gallery calls
   Experimental and Voltaic Staff, NomNom calls `Pristine` and `StaffAtlas`.
4. **Unrolled tools read as Rifle C.** A tool with every stat at zero falls inside Rifle C
   and outside every other C range, so scripted tools — the one from a crashed ship, an
   expedition's starter — are typed as rifles. The stats carry no other signal to go on.
5. **No corvette fixture**, so the `Base` rows are exercised only by a synthesised object.
6. **`BinaryData` in payloads.** No fixture contains any. If some entity type does carry
   binary fields, every adapter needs to prove it survives.
