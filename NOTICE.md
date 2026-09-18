# Third-party notices

NMS-Vault is licensed under the **GNU Affero General Public License v3.0 or later**
(see [LICENSE](LICENSE)). It incorporates and builds on the work below.

---

## NMSE (No Man's Save Editor)

- <https://github.com/vectorcmdr/NMSE>
- Copyright © the NMSE authors
- **GNU Affero General Public License v3**

NMS-Vault's JSON engine is ported from NMSE. **The ported files have been modified.** Each
carries a header naming its original path and what changed. As of this writing:

| File here | Original in NMSE | Modifications |
|---|---|---|
| `src/NmsVault.Json/JsonObject.cs` | `Models/JsonObject.cs` | Dropped the dead `Listener` property; replaced `ExportToFile`/`ImportFromFile` with `ToExportString`/`FromBytes`; namespace |
| `src/NmsVault.Json/JsonArray.cs` | `Models/JsonArray.cs` | Dropped the dead `Listener` property; namespace |
| `src/NmsVault.Json/JsonParser.cs` | `Models/JsonParser.cs` | Line endings hardcoded to LF; static key-intern pool removed; static mapper singleton replaced by an explicit `autoDetect` parameter; namespace |
| `src/NmsVault.Json/JsonReader.cs` | `Models/JsonReader.cs` | Namespace |
| `src/NmsVault.Json/JsonException.cs` | `Models/JsonException.cs` | Namespace |
| `src/NmsVault.Json/RawDouble.cs` | `Models/RawDouble.cs` | Namespace |
| `src/NmsVault.Json/BinaryData.cs` | `Models/BinaryData.cs` | Namespace |
| `src/NmsVault.Json/JsonNameMapper.cs` | `Data/JsonNameMapper.cs` | Replaced `Load(string)` with `LoadEmbedded()`; namespace |
| `tests/NmsVault.Json.Tests/JsonModelTests.cs` | `NMSE.Tests/JsonModelTests.cs` | Namespace |

## `mapping.json` — MBINCompiler

- <https://github.com/monkeyman192/MBINCompiler>
- Copyright © monkeyman192 and contributors
- **MIT License**

`src/NmsVault.Json/mapping.json` is the No Man's Sky obfuscated-key mapping table derived from
MBINCompiler. It reached this project via NMSE's `Resources/map/mapping.json`.

## Planned dependencies

Not yet referenced; listed here so the obligations are known before they land.

### libNOM.collect / libNOM.map

- <https://github.com/zencq/libNOM.collect> · <https://github.com/zencq/libNOM.map>
- Copyright © Christian Engelhardt (zencq)
- **GNU General Public License v3.0 only**

AGPLv3 §13 and GPLv3 §13 grant reciprocal permission to combine works under these two licences.
AGPL, being the more restrictive, governs the combined work.

libNOM's README credits **Dr. Kaii** (NMS Companion) for format collaboration; that credit is
carried forward here.

### Newtonsoft.Json

- <https://github.com/JamesNK/Newtonsoft.Json>
- Copyright © James Newton-King
- **MIT License**

---

## Save editor formats

NMS-Vault reads and writes export formats belonging to these projects. It is not affiliated with,
endorsed by, or derived from any of them beyond format interoperability.

- **goatfungus NMSSaveEditor** — <https://github.com/goatfungus/NMSSaveEditor>
- **NMS Companion** by Dr. Kaii — <https://www.nexusmods.com/nomanssky/mods/1879>
- **NomNom** by zencq — <https://github.com/zencq/NomNom>
- **NMSE** by vectorcmdr — <https://github.com/vectorcmdr/NMSE>

No Man's Sky is a trademark of Hello Games. This project is unaffiliated with Hello Games.

---

## Gallery content

Item data and images under `wwwroot/gallery/` are **content, not software**, and are licensed
separately — see `wwwroot/gallery/LICENSE`.
