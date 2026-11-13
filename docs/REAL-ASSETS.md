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
| Path tiles (one per path block and placement) | 2163 | 1957 |
| Path graph nodes (car / ped / water) | 4947 (1971 / 2976 / 0) | 9642 (2973 / 6291 / 378) |
| Path graph adjacency entries | 10272 | 20350 |
| Cross-tile crossings / unpaired externals | 3200 / 325 | 2456 / 178 |
| `CCarPathLink` entries / lane fallbacks | 2079 / 4 | 3064 / 4 |
| Links flagged `bCrossesRoad` | 512 | 36 |
| Path audit (`--verify-paths`) | consistent, longest 186.61, no long links | consistent, 26 long links (all water, max 329.02) |
| Route probe (ground to 200 units away) | 26 hops, 728.49 units | 282 hops, 4847.98 units |
| Car ground delta (preview / full map) | +0.46 max / −19.8 min (multi-level roads) | −0.72 max / −0.29 avg |
| Car lanes per direction (max left / max right) | 2 / 2 (1209 multi-lane links, 573 one-way) | 3 / 3 (231 multi-lane links, 629 one-way) |
| Traffic lights scanned → signalled links (phase 1 / phase 2, direction-flagged) | 333 → 313 (161 / 152, 172) | 271 → 271 (155 / 116, 140) |
| Light-controlled ped crossings (`bTrafficLight`) | 402 | 20 |
| Ped waiting ticks (60 peds, 1200 / 3000 ticks) | 491 / 6953 | 0 (routes never reach a flagged crossing) |
| `--walk 812 -936 36 1 0 3`, 120 ticks (hits / travelled) | 57 / 11.9, stopped by a railing | — |
| `--walk 700 -700 10 0 1 3`, 300 ticks | 0 hits / 30 units, z = 10 | — |
| `--walk -864 -575 10 1 0 3`, 200 ticks (VC) | — | 0 hits / 20 units, z = 9.8875 |
| `--play` + held W in the preview | `World-Play: model=cop`, `World-Play-Final: hits=0 travelled=0.53` | — |
| Actor-ticks held at lights (8 cars / 4 cars, 3000 ticks) | 3177 | 1936 |
| Actor-ticks held with car following (`VehicleGap`, 10 / 20 cars, 3000 ticks) | 4898 (completed 7) | 19555 (completed 6, 26 at 6000 ticks) |
| Car ground delta after lane offsets (`--spawn-cars 6`, 1200 ticks) | +0.309 max / −17.9 min | +0.115 max / −0.184 min |
| Spawned actors, 120 ticks (`--spawn-cars 2 --spawn-peds 3`) | 5 actors, `vehicleModel=landstal pedModel=cop`, travelled 73.77 | 5 actors, `landstal`/`cop`, travelled 114 |
| Path graph extern slots / empty slots | 6726 / 14691 | 5090 / 8752 |
| Path graph components (car / ped / water) | 109 (3 / 106 / 0) | 42 (4 / 32 / 6) |
| Single-node components / largest component | 20 / 1967 | 0 / 5102 |
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

`PathGraph` собран на обеих играх и проверен real-asset probes: III даёт 2163 тайла, 4947 узлов (1971 car / 2976 ped), 10272 записи симметричных связей, 3200 межтайловых переходов и 325 непарных extern'ов; VC — 1957 тайлов, 9642 узла (2973 / 6291 / 378 water), 20350 записей, 2456 переходов и 178 непарных extern'ов. `--query-path-nearest` возвращает ожидаемые intern-узлы рядом с ground probe: III car 1969 на `(808.9525,-942.0005,36.4859)` и ped 2245, VC car 139 на `(-833.21875,-565.6431,9.885688)`, ped 9176 и water 7027 на `(-744.0563,-530.2438,6)`. Пустой участок карты честно даёт `found=false`.

Extern-слияние faithful по порогу: в VC 2456 переходов покрывают 4912 из 5090 extern-слотов, в III 3200 переходов покрывают 6400 из 6726. Непарными остаются 325 extern'ов III — это реальные тупики и разрывы дорог, а не артефакт реконструкции. Один extern в III не разрешается в intern-узел (`unresolvedExtern=1`).

Ранее опубликованные числа (1634 перехода, 3457 непарных, 1480 компонент) были следствием ошибки в повороте инстансов, а не свойством данных: `StaticInstance` строил поворот по неединичной оси (`Quaternion.FromAxisAngle` её не нормирует, а IPL хранит векторную часть единичного кватерниона — 0.707107 для поворота на 90°). Оригинал нормирует ось внутри `RwMatrixRotate`, теперь так же делает и загрузчик, поэтому повёрнутые объекты получают правильный угол и не сжимаются по XY. VC это не затрагивало: все его path-группы detached и transform не используют.

Flood-fill группы показывают, насколько граф связен: VC сходится в 42 компоненты (4 car / 32 ped / 6 water) без одиночных узлов при крупнейшей компоненте в 5102 узла, а III — в 109 компонент (3 car / 106 ped), из которых 20 одиночных, и крупнейшая car-компонента содержит 1967 узлов из 1971. То есть после исправления поворота дорожная сеть III практически связна. `trafficLightType` и удаление изолированных ped-узлов (`CPathFind` выкидывает их из `m_pathNodes`) пока не реализованы; dead-end флаги car-узлов сделаны (III 158 из 1971, VC 199 из 2973).

`--path-merge-distance R` позволяет пересобрать граф с другим порогом схлопывания extern-пар; default 1 unit — оригинальный, всё остальное помечается как divergence. Кривая на III ведёт себя как и должна: 1 → 109 компонент, 2 → 107, 3 → 281, 5 → 289, 10 → 480, то есть расширение порога начинает склеивать несвязанные линии и дробит крупные компоненты.

`--query-path-route` проверяет маршрутизацию на этих же данных: III находит car-маршрут в 26 переходов / 728.49 unit между `(812,-936,36)` и `(700,-700,36)`, VC — ped-маршрут в 282 hops / 4847.98 unit внутри крупнейшей компоненты (компонента 2).

Car-связи получают `CCarPathLink`-данные: III 2079 записей и всего 4 стороны, ушедшие в дефолт 1/1; VC 3064 записи и тоже 4 fallback'а, то есть полосы почти везде известны из extern-переходов. Направление и midpoint считаются по тем же правилам, что в оригинале: направление всегда к нижнему индексу узла, одна запись общая для обоих направлений связи.

Спавн актёров проверяется на обоих деревьях: `--spawn-cars 2 --spawn-peds 3` даёт 5 actor'ов с моделями `landstal`/`cop` (III 73.77 unit за 120 тиков, VC 114) — то есть подхватывается первый реальный рекорд своего вида, а не `null`-модель id 0, которая есть в IDE обоих городов и не имеет меша. Ped DFF в III состоит из двух clump'ов (`cop.dff`: 2 clumps, 25 atomics, 1096 треугольников), поэтому статическая модель конвертируется по всем clump'ам; в world preview стартовая точка на эстакаде оставляет ped-сеть под настилом, так что пиксельное доказательство ped-меша даёт `--model cop`, а не кадр мира.

Рециклинг трафика проверяется там же: `--spawn-cars 3 --recycle --spawn-car-speed 40` за 3000 тиков даёт III `spawned=despawned=recycled=12` (11991 unit), VC — 13 (11992.68 unit), при этом `Path-Follow: vehicles=3 peds=0 actors=3` не меняется, то есть население держится на target, а не растёт. Пешеходы рециклируются тем же модулем (III `--spawn-peds 3 --recycle --spawn-ped-speed 10` → 4 замены). Узлы замен выбираются детерминированным round-robin со stride, а не случайно по зонам, как в `CPopulation`.

Полосы тоже взяты из реальных данных, а не выдуманы: `Path-Lanes` показывает `maxLeft=maxRight=2`, 573 one-way и 1209 multi-lane связей в III и `maxLeft=maxRight=3`, 629 one-way и 231 multi-lane в VC — то есть смещение машины в сторону от линии узлов действительно различается по дорогам. После включения смещения `VehicleGroundProbe` на `--spawn-cars 6` за 1200 тиков даёт `deltaMax=0.309` (III) и `0.115` (VC), то есть колёса по-прежнему на дороге; `deltaMin=-17.9` в III — те же многоуровневые развязки, что и раньше.

Светофоры тоже считаются из размещённых объектов, а не назначаются по эвристике: III находит 333 светофора и помечает 313 car-связей (161 фазы 1, 152 фазы 2, 172 с флагом направления), VC — 271 светофор и 271 связь (155 / 116 / 140). На реальных прогонах машины действительно ждут: 8 машин III за 3000 тиков дают `holds=3177` при `completed=6`, 4 машины VC — `holds=1936`. Фазы идут по симуляционным миллисекундам (30 Hz), а не по wall-clock `CTimer`, поэтому `State@N` детерминирован.

С `VehicleGap` (6 unit) за светофором встаёт очередь: III 10 машин за 3000 тиков — `holds=4898`, `completed=7`; VC 20 машин — `holds=19555`, `completed=6`, а на 6000 тиках `completed=26` при том же числе живых. То есть это не затор: очередь рассасывается на зелёном, а маршруты продолжаются. Пешеходов зазор не касается — они по-прежнему идут по линии узлов без остановок.

Пешеходные переходы со светофором: скан помечает 402 таких связи в III и 20 в VC (`pedLinks`), и III-пробы действительно показывают ожидание — 60 пешеходов за 1200 тиков дают `holds=491`, за 3000 — `holds=6953`. В VC те же 140 пешеходов (это предел ped-пула) за 2500 тиков не пересекают ни одного помеченного перехода, поэтому на реальных данных ожидание там не наблюдается: механизм покрыт синтетическим тестом, а `pedLinks=20` фиксирует, что скан переходы нашёл.

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
