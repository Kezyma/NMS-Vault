# data

Everything the gallery is built from. The site has no other source: `nmsvault-ingest build`
reads this folder and writes `src/NmsVault.Web/wwwroot/gallery`, which is generated output and
is not committed.

## Layout

One folder per kind, and everything about an item sits beside the export it came from, under
the same name:

```
Starships/
  [START] Rasamama S36.nmsship     the export itself
  [START] Rasamama S36.json        its metadata
  [START] Rasamama S36.jpg         its first picture
  [START] Rasamama S36-2.jpg       its second
Multitools/
Companions/
```

- **Exports** are `.nmsship`, `.nmstool`, `.nmspet` or `.nmsfrig` from NMSE, and `.shp`, `.mlt`
  or `.cmp` from NMS Companion or NomNom. The format is detected from the content, not the
  extension.
- **Pictures** may be `.webp`, `.png`, `.jpg` or `.jpeg`, and are re-encoded to WebP at 1600px
  on import. The first has no number; further ones count from two. **The scan stops at the
  first gap**, so `-2` and `-4` without a `-3` silently loses the `-4`.
- **Metadata** is the `.json` file beside the export. `docs/item-template.json` is a blank one
  with every field explained.

## Two fields worth knowing about

**`Id`** is the item's address in the published site, and the name its stored pictures take. It
is pinned in every file here rather than derived from the display name, for two reasons: two
ships are both called `Rasamama S36` and would otherwise claim the same id, and renaming an
item should not move a URL somebody has bookmarked.

**`DateAdded`** is here rather than stamped at import because the gallery is rebuilt from
scratch on every build. Left to the tool, every item would be dated the moment the build ran.

## Adding something

Drop the export in the right folder, put its pictures and a `.json` beside it, and build. The
gallery is rebuilt from this folder every time the web project builds, so there is no separate
import step.

A file here that no export matches is reported on every run - the usual cause is a `.json`
whose name differs from its export by a bracket, which otherwise fails silently by doing
nothing at all.
