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
| Navigation zones (without root) | 42 | 12 |
| Information zones (without root) | 42 (shared array) | 165 |
| Map zones (without root) | 21 | 33 |
| Cull zones | 744 | 631 |
| Occlusion volumes | 0 | 344 |
| Path groups / nodes | 870 / 10440 | 1957 / 23484 |
| Detached / water path groups | 0 / 0 | 1957 / 108 |
| Unresolved attached path groups | 34 | 0 |
| Path tiles (one per bound instance) | 2163 | 1957 |
| Path graph nodes (car / ped / water) | 4947 (1971 / 2976 / 0) | 9642 (2973 / 6291 / 378) |
| Path graph adjacency entries | 7140 | 20350 |
| Cross-tile crossings / unpaired externals | 1634 / 3457 | 2456 / 178 |
| Links flagged `bCrossesRoad` | 512 | 36 |
| Route probe (ground to 200 units away) | disconnected (components 977 / 728) | 282 hops, 4847.98 units |
| Path graph extern slots / empty slots | 6726 / 14691 | 5090 / 8752 |
| Path graph components (car / ped / water) | 1480 (482 / 998 / 0) | 42 (4 / 32 / 6) |
| Single-node components / largest component | 936 / 254 | 0 / 5102 |
| Unresolved path groups | 34 | 0 |
| Nearest car node at the ground probe | 1969 `(808.9525,-942.0005,36.4859)` | 139 `(-833.21875,-565.6431,9.885688)` |
| Nearest ped node at the ground probe | 2245 `(843.9595,-847.0115,14.0577)` | 9176 `(-863.1125,-571.675,10.0465622)` |
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
| DFF Skin | 18 | 275 |
| DFF old Skin | 18 | 161 |
| DFF split Skin | 0 | 114 |
| DFF HAnim | 360 | 23884 |
| DFF MatFX materials | 10 | 2960 |
| NullRenderer PLAYER atomics | 16 | 1 |
| NullRenderer PLAYER triangles | 1550 | 1355 |
| PLAYER resolved textures | 16 | 1 |
| PLAYER texture RGBA bytes | 4194304 | 262144 |
| TXD entries scanned | 718 | 1368 |
| Failed TXD entries | 0 | 0 |
| D3D8 textures decoded | 7686 | 12023 |
| Unsupported PS2 rasters skipped | 137 | 0 |
| Mip levels decoded | 7686 | 18451 |
| Pixels decoded | 82338816 | 334757867 |
| Uncompressed textures | 7686 | 28 |
| DXT1 textures | 0 | 10846 |
| DXT3 textures | 0 | 1149 |

`LoadStandard()` завершается успешно на обеих играх и на обоих runtimes. Отсутствующих моделей для прочитанных IPL нет. Полные `--dff-scan` и `--txd-scan` проходят на всех nonempty DFF/TXD. Проверены отдельно III `loplyguy.dff` (legacy `0x30200`), `peds.dff` (22 clumps) и VC `wheels.DFF` (20 geometries), VC `FIREMAN.DFF` (split Skin + HAnim), loose `particle.txd` обеих игр, III `PLAYER.TXD`, VC DXT1 `WMYBE.TXD` (9 levels), DXT3 `AIRPLAN.TXD` и uncompressed `DOCENTRANCE.TXD`. VC дополнительно читает 30 COLL-ресурсов из IMG до первого IPL. Реальные ground queries прошли:

- III `(812, -936)` → `ind_customroad003`, `z=35.9520454`; zone `ROADBR1:Navig`, path group 0/node 0 `(447.332764,-628.411255,15.1032)`;
- VC `(-864, -575)` → `lh_icecream01`, `z=10.0659924`; navigation `HAVANA:Default`, info `ICCREAM:Info`, detached path `(-866.6312,-652.45,10.067687)`.

Полученные warnings относятся только к явно не реализованным directives:

- III: `TEXDICTION`, `SPLASH`, `MODELFILE`;
- VC: `TEXDICTION`, `MODELFILE`, `SPLASH`.

Entity-пулы заполняются из IPL по правилам оригинала и укладываются в лимиты без overflow: III `buildings=5140/5500 dummies=2336/2802 treadables=1213/1214 objects=0/450 rejected=0`, VC `buildings=6778/7000 dummies=2336/2340 treadables=0/1 objects=0/460 rejected=0` при 167/326 моделях в `OBJECT.DAT`. Это самый сильный сигнал, что классификация совпадает с `CFileLoader::LoadObjectInstance`, а не подогнана: treadable-ёмкость III исчерпана на 1213 из 1214.

`MAPZONE` III теперь загружается. III unresolved paths — attached groups без соответствующего instance, выводятся через `--verbose`; это не ошибка parser. Полного `CPathFind.PreparePathData` ещё нет.

`PathGraph` собран на обеих играх и проверен real-asset probes: III даёт 2163 тайла, 4947 узлов (1971 car / 2976 ped), 7140 записей симметричных связей, 1634 межтайловых перехода и 3457 непарных extern'ов; VC — 1957 тайлов, 9642 узла (2973 / 6291 / 378 water), 20350 записей, 2456 переходов и 178 непарных extern'ов. `--query-path-nearest` возвращает ожидаемые intern-узлы рядом с ground probe: III car 1969 на `(808.9525,-942.0005,36.4859)` и ped 2245, VC car 139 на `(-833.21875,-565.6431,9.885688)`, ped 9176 и water 7027 на `(-744.0563,-530.2438,6)`. Пустой участок карты честно даёт `found=false`.

Extern-слияние faithful по порогу: в VC 4918 из 5090 extern'ов имеют соседа ближе 1 unit и 2456 переходов покрывают почти все возможные пары; в III таких extern'ов 3295 из 6726, поэтому 3457 остаются непарными — это состояние данных и greedy-слияния оригинала, а не скрытая ошибка. Один extern в III не разрешается в intern-узел (`unresolvedExtern=1`).

Flood-fill группы показывают, насколько граф связен: VC сходится в 42 компоненты (4 car / 32 ped / 6 water) без одиночных узлов при крупнейшей компоненте в 5102 узла, а III распадается на 1480 компонент (482 car / 998 ped), из которых 936 — одиночные узлы без единой связи, крупнейшая всего 254 узла. Это прямое следствие непарных extern'ов, а не отдельная ошибка. `m_connections`-флаги (`bCrossesRoad`, `bTrafficLight`), lane direction/counts и route search не реализованы, поэтому маршруты не строятся.

`--path-merge-distance R` позволяет пересобрать граф с другим порогом схлопывания extern-пар; default 1 unit — оригинальный, всё остальное помечается как divergence. Проверка на III: 1 → 1480 компонент, 5 → 1356, 10 → 1018, 20 → 1437 с падением крупнейшей компоненты с 442 до 89 узлов — то есть радиус не «лечит» фрагментацию, а после 10 unit начинает склеивать чужие линии. Сравнение полного transform с translation-only дало почти одинаковые распределения расстояний, значит ошибка не в поворотах инстансов: extern'ы III действительно не совпадают в пределах 1 unit. Открытый вопрос требует сверки с набором тайлов оригинала (статический реверс `gta3.exe`), а не новых допущений.

`--query-path-route` проверяет маршрутизацию на этих же данных: VC находит путь в 282 hops / 4847.98 unit внутри крупнейшей компоненты (ped, компонента 2), а III между `(812,-936,36)` и `(-700,...)` честно отвечает `disconnected` с номерами компонент 977 и 728. Это ожидаемо при 3457 непарных extern'ах и не маскируется: маршрут строится только по реально существующим связям.

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
