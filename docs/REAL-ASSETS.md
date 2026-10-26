# Проверка на оригинальных ресурсах

Проверено **2026-10-26** на двух локальных деревьях в ignored-каталоге `refs/`:

- `refs/Grand Theft Auto 3`;
- `refs/Grand Theft Auto Vice City`.

Файлы не копируются в Git и не изменяются тестами. Запускались Release-команды `SharpGTA.Headless` на **.NET Framework (хост 4.8, target net40)** и **.NET 9.0**. Это не запуск на Windows XP.

## Golden summary текущих деревьев

| Проверка | GTA III | GTA Vice City |
|---|---:|---:|
| DAT | 2 | 2 |
| IDE | 17 | 32 |
| IPL | 14 | 36 |
| Loose COLL files | 18 | 3 |
| COLL files from IMG | 0 | 30 |
| IMG catalogs | 1 | 1 |
| IDE models | 3081 | 4418 |
| COLL models loaded | 2982 | 4245 |
| Static IPL instances | 8689 | 9114 |
| GXT main keys | 2649 | 2449 |
| GXT mission tables | 0 | 78 |
| `PLAYER.DFF` RW chunks | 329 | 103 |
| RW nesting depth | 7 | 7 |
| DFF entries scanned | 3138 | 4617 |
| Empty DFF placeholders | 4 | 0 |
| Failed nonempty DFF | 0 | 0 |
| Decoded clumps | 3220 | 4617 |
| Decoded geometries | 6845 | 6510 |
| Decoded triangles | 1012392 | 2211176 |
| Clump length mismatches handled | 575 | 0 |
| TXD entries scanned | 718 | 1368 |
| Failed TXD entries | 0 | 0 |
| D3D8 textures decoded | 7686 | 12023 |
| Unsupported PS2 rasters skipped | 137 | 0 |
| Mip levels decoded | 7686 | 18451 |
| Pixels decoded | 82338816 | 334757867 |
| Uncompressed textures | 7686 | 28 |
| DXT1 textures | 0 | 10846 |
| DXT3 textures | 0 | 1149 |

`LoadStandard()` завершается успешно на обеих играх и на обоих runtimes. Отсутствующих моделей для прочитанных IPL нет. Полные `--dff-scan` и `--txd-scan` проходят на всех nonempty DFF/TXD. Проверены отдельно III `loplyguy.dff` (legacy `0x30200`), `peds.dff` (22 clumps) и VC `wheels.DFF` (20 geometries), loose `particle.txd` обеих игр, III `PLAYER.TXD`, VC DXT1 `WMYBE.TXD` (9 levels), DXT3 `AIRPLAN.TXD` и uncompressed `DOCENTRANCE.TXD`. VC дополнительно читает 30 COLL-ресурсов из IMG до первого IPL. Реальные ground queries прошли:

- III `(812, -936)` → `ind_customroad003`, `z=35.9520454`;
- VC `(-864, -575)` → `lh_icecream01`, `z=10.0659924`.

Полученные warnings относятся только к явно не реализованным directives:

- III: `TEXDICTION`, `SPLASH`, `MODELFILE`, `MAPZONE`;
- VC: `TEXDICTION`, `MODELFILE`, `SPLASH`.

## Важные границы проверки

- **VC collision сейчас загружается целиком и синхронно до первого IPL:** bounds-based streaming/eviction полноценного `CColStore` ещё не реализованы.
- DFF декодируется в CPU-side static geometry/materials/BinMesh и проверен по всем nonempty IMG entries. III имеет четыре zero-sized placeholders: `plaster.dff`, `g.dff`, `stu_man.dff`, `stu_wom.dff`. Это не malformed models.
- TXD dictionaries и все D3D8 mip levels прочитаны и декодированы в RGBA. III PS2-rasters (`137`) пропущены явно и не считаются PC decode success. Визуальное сравнение кадров пока не выполняется; PAL4/DXT2/DXT4/DXT5 не встречались в этих деревьях (DXT5 проверен только synthetic).
- Загруженные models/instances не означают playable simulation: SCM, dynamic entities, physics, AI и renderer отсутствуют.
- Это summary именно локальных resource variants, не спецификация одинаковых counts для всех переводов/редакций/модов. Test script проверяет инварианты и успешное чтение, а не требует эти числа от любого пользовательского дерева.
- Повторяемость runtime hash проверена, но world/asset state пока не сериализуется в `State@N`.

Повторная проверка:

```powershell
dotnet build SharpGTA.sln -c Release
powershell -NoProfile -File tools/test-real-assets.ps1
```

Если оба дерева отсутствуют, скрипт печатает `SKIP` и не выдает это за пройденный real-asset test. Обычные synthetic tests не зависят от `refs/`.
