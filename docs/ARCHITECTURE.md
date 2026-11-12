# Архитектура текущего фундамента

## Зависимости

```text
SharpGTA.Core
  ↑             ↑             ↑
Games.III     Games.VC    SharpGTA.D3D9 / Launcher
  ↑             ↑
     SharpGTA.Headless
             ↑
       SharpGTA.Tests ───────→ D3D9 ABI/BMP tests
```

Core не знает конкретные игровые классы, Win32, Direct3D или RenderWare objects. Headless выбирает `IIIDefinition.Instance`/`VCDefinition.Instance` и собирает runtime; Headless не ссылается на D3D9. D3D9 и Launcher используют общий Core/branding/frontend state и существуют как отдельные executables. Собственные DLL проекта не являются сторонними зависимостями; shipping состоит только из этих сборок и .NET Framework/system API.

## GameDefinition вместо общих numeric enums

`GameDefinition` содержит identity, DAT/IMG paths, initial semantic level, weather count, collision storage kind, `DataSchema` и `GameLimits`. III и VC имеют независимые объекты. Например, VC `WEAPON` в model enum сдвигает последующие raw values, а weapon/level/opcode numbers тоже различаются; единый managed cast был бы ошибкой.

`DataSchema` описывает только реальные layout differences: VC vehicle `animFile`, ped anim/radio, `weap`, IPL area и numeric path type. Общие readers строят DTO. Runtime binding выполняется отдельно в `ModelRegistry`/`StaticWorld`. Raw DTO sections сохраняются для ещё не перенесённых path/2dfx/zone/cull/occl handlers.

## Content pipeline

```text
PhysicalGameFileSystem (read-only, relative, case-insensitive)
  → bounded text/binary readers
  → DTO / ImgDirectory / GxtArchive / ColModel
  → ModelRegistry + CollisionRepository + StaticWorld
```

`GameDataLoader.LoadStandard()` открывает initial `MODELS/GTA3.IMG` и sibling DIR, затем выполняет `DEFAULT.DAT` и main DAT в исходном порядке. Поддержаны `IDE`, `IPL`, `COLFILE`, `CDIMAGE`; остальные директивы попадают в ограниченный `LoadReport`, а не имитируют выполненную работу.

`ImgArchive(Stream, ...)` держит seekable file stream и читает только выбранный sector range. Верхние 8 бит raw offset выбирают image, младшие 24 — sector. Первый duplicate name выигрывает. `ImgCatalog.Dispose` закрывает потоки.

GXT возвращает raw 16-bit code units и их прямое char-представление без ложного заявления о Unicode/codepage decoding. VC `TABL` mission tables читаются по absolute offsets. GXT keys ищутся ordinal case-sensitive.

`DffReader` создаёт CPU-side DTO без GPU: Clump/frames/GeometryList/Atomics, platform-independent geometry, morph targets, materials и texture references, BinMesh list/strip. Поддержаны legacy embedded Atomic, multi-clump files и известные retail Clump parent-length mismatches через count-driven parsing. `DffPluginReader` дополнительно читает HAnim, old/split Skin с weights/inverse matrices/RLE и MatFX texture effects; unknown extensions пропускаются по declared length, native geometry не выдаётся за static mesh. Это binary DTO, ещё не animation/skinning runtime. `--dff-scan` декодирует каждый nonempty DFF entry без удержания всего набора в памяти.

`TxdReader` читает PC D3D8 TextureDictionary, fixed names, palettes и все mip levels с проверкой фактических размеров. `TxdPixelDecoder` выдаёт tightly packed RGBA8 по требованию: uncompressed C8888/C888/C1555/C565/C4444/C555/LUM8/PAL8 и DXT1/DXT3, плюс synthetic-covered DXT5. Raw levels остаются доступны для будущего GPU upload. PS2, PAL4 и DXT2/DXT4 не маскируются под PC decode; scan пропускает PS2 по явной policy и сообщает counts. `--txd-scan` декодирует и освобождает каждый level, не удерживая все изображения.

## Streaming slots

`Assets/StreamingPool<TKey,TResource>` воспроизводит нужную часть `CStreaming` на managed уровне: `NotLoaded`, `InQueue`, `Loaded`, `Failed`; refcount, priority/FIFO request order, pinning/unpin, LRU eviction и synchronous `LoadAllRequested`. Loader/unloader остаются delegates, поэтому Core не знает о D3D9, а I/O/GPU ownership не прячется внутри пула. Capacity измеряется числом ресурсов, не байтами. `Deferred` оставляет request в queue при временной нехватке capacity/dependency и не считается permanent failure. Ошибка loader оставляет `Failed` и message, а не подменяет ресурс.

World host использует два пула: модели (default 512, CLI `--stream-budget`) и TXD (default 96, CLI `--txd-budget`). Каждый кадр освобождает refs прошлого кадра, запрашивает unique visible models по distance priority и синхронно загружает их. Eviction вызывает renderer unload для shared mesh buffers и TXD texture context, затем очищает CPU clump/texture caches. Если detailed model недоступен, world host отдельно запрашивает near LOD fallback и скрывает его, когда detailed снова загружен. Полный `CStreaming` channel state и dynamic entity/time streaming пока отсутствуют.

## Async byte delivery

`IO/AsyncByteCache` — отдельный Core-слой между диском и decoder: два background thread только читают bytes по строковому ключу, а `Pump`/`TryTake`/`WaitFor`/`Remove` вызываются на simulation thread. Бюджет задаётся в байтах (`--byte-budget`, 1..1024 MiB, default 64 MiB) и enforced как LRU. Payload больше самого бюджета не становится ошибкой ассета: без ожидающего потребителя он просто не кэшируется, а ожидающему доставляется напрямую и удаляется сразу после decode. `WaitFor` защищает ожидаемые bytes от eviction до `Remove` и повторно запрашивает их, если они были вытеснены до decoder. Read failure не повторяется автоматически и снимается только явным `Forget`, поэтому битый ключ не перечитывается каждый кадр. Никакой decode, создание объектов или mutation мира не происходят на worker threads.

World host кодирует ключи как `dff:NAME` / `txd:NAME`, prefetch-ит ring `radius * 1.5` вперёд и декодирует `DffReader`/`TxdReader` на simulation thread, освобождая bytes сразу после `Remove`. Pending bytes дают `Deferred`, а не `Failed`. Первый кадр ожидает доставку per-model, поэтому startup детерминирован и малый byte budget не оставляет пустую сцену; последующие кадры только `Pump`, из-за чего новый контент может появиться на кадр позже — это и есть настоящий streaming lag. IDE/IPL/COL/IMG loading, gameplay streaming и полный `CStreaming` lifecycle остаются синхронными.

## Entities и время

`World/EntityPool<T>` — fixed-capacity пул с оригинальным `CPool` handle encoding: индекс в старших битах, 7-bit generation + free flag в младшем байте. `New` ищет свободный слот от текущего курсора с wrap-around, `Delete` откатывает курсор назад, `GetAt` возвращает null для `-1`, чужих и устаревших handle'ов, а generation переполняется на 128 переиспользованиях — как в оригинале. Пул не создаёт объекты сам: `New(entry)` атомарно резервирует слот и сохраняет запись, поэтому полуинициализированных слотов не бывает.

`TimeObjectController` отслеживает IDE `tobj` инстансы, у которых открыто часовое окно. Оригинал держит entity и гейтит видимость в renderer/streaming коде; этот preview моделирует то же состояние слотом пула: инстанс держит слот ровно пока открыто окно, поэтому аллокация и освобождение следуют за игровыми часами. Окно — это `CClock::GetIsTimeInRange`: часы, конец эксклюзивен, переход через полночь поддержан. Время берётся из существующего `Runtime.GameClock`, который шагает детерминированным 30 Hz driver'ом (одна игровая минута на 30 кадров), а не по wall clock. Пары `_nt`/`_dy` резолвятся как `CTimeModelInfo::FindOtherTimeModel`: первый токен меняется на противоположный, хвост после токена отбрасывается, а switched-имя ищется точно (оригинал сравнивает префикс).

`StaticWorldScene` учитывает time-фильтр до draw distance и LOD handoff, поэтому скрытые timed-модели не запрашиваются у стримера и не попадают в draw calls. Timed-модели участвуют в suffix LOD-парах и draw-distance handoff так же, как обычные, потому что оригинальный `IsSimple()` включает `MITYPE_TIME`. Неtimed placements всегда видимы; pool overflow не скрывает остальной мир, а считается в diagnostics. `OtherTimeModel` уже разрешён, но потребителя в renderer'е ещё нет.

## Entity pools и классификация placements

`WorldEntities` держит по пулу на вид entity с размерами из `GameLimits` (III: buildings 5500, objects 450, dummies 2802, treadables 1214, peds 140, vehicles 110; VC: 7000/460/2340/1/140/110). Каждый IPL placement получает числовой handle в пуле своего вида и хранит его в `StaticInstance.Handle`; вид не дублируется в записи, а определяется пулом, поэтому разыменование требует совпадающий `WorldEntityKind`, как `CPools::GetBuilding` против `CPools::GetObject`.

Классификация повторяет `CFileLoader::LoadObjectInstance`: модель, перечисленная в `OBJECT.DAT`, становится `CDummyObject`, модель из path-заголовка (наш `PathWorld.IsPathObject`, оригинальный `ThePaths.IsPathObject`) — `CTreadable`, остальные — `CBuilding`. Именно это объясняет, почему оригинальные пулы (5500/2802/1214) вмещают карту почти без запаса: на реальных данных выходит III 5140/2336/1213 и VC 6778/2336/0 при `rejected=0`. Object pool остаётся пустым до gameplay objects, ped/vehicle пулы пока только ёмкости.

`ObjectData`/`ObjectDataReader` читают `DATA/OBJECT.DAT`: 11 колонок `CObjectInfo`, комментарии `;` в любом месте строки, остановка на `*`-sentinel, дополнительные VC-колонки игнорируются как в `sscanf`, повторяющиеся модели перезаписываются последней строкой. Битая строка и отсутствие файла дают warning в `LoadReport`, а не валят весь load: оригинал продолжает разбор молча, поэтому modded-дерево не должно ломать карту. Полная перегрузка пула (`New` вернул -1) не скрывается: placement остаётся в мире без handle, а счётчик `rejected` попадает в diagnostics. Классификация фиксируется при `Add`, поэтому `SetEntityClassification` после первого `Bind` отклоняется явно; path-группы из IPL обрабатываются после инстансов того же файла, как и в оригинале, а III/VC хранят path-заголовки в IDE.

## Dynamic entities

`WorldEntity` — общий предок `StaticInstance` (IPL placement) и `DynamicEntity` (gameplay object/ped/vehicle): модель, `Transform`, `Position`, per-pool `Handle`, `ScanCode`, `WorldBounds`. Пул на вид один, поэтому gameplay object и placement делят `CObject`/`CBuilding`-семантику и разыменовываются только вместе с `WorldEntityKind`.

Gameplay entities создаются `StaticWorld.TrySpawnEntity` (object/ped/vehicle pool), индексируются в тот же секторный грид, что и placements (`QueryEntities` возвращает смешанный набор, `QuerySectors` — только placements), и освобождаются `DespawnEntity` с обычной generation-защитой handle. `DynamicEntityModule` шагает их из `SimulationRuntime`: `AdvanceDynamic(deltaSeconds)` снимает сущность с грида, интегрирует линейную скорость, ставит обратно и считает смены сектора (`SectorMoves`); модуль реализует `IStateSerializable`, поэтому позиции и скорости входят в `State@N`. Сущность, ушедшая за границу сетки, попадает в `unindexed` и, как и placements там, всегда возвращается запросами — сетка не может её проиндексировать.

`WorldResourceManager.BuildDynamic` рисует gameplay entities через те же model/TXD pools, что и карту: модель запрашивается по имени, clump декодируется один раз и переиспользуется GPU-кэшем, поэтому движущийся object не создаёт вторую копию геометрии. `StaticWorldScene.IsEntityVisible` применяет view radius и собственный draw distance модели (без LOD-пары и часового окна), а кадр склеивает статические и динамические draw calls (статика первой, чтобы не менять прежний порядок блендинга).

Кадр начинается с `BeginFrame()`: один `ReleaseAll`/`Pump` на оба прохода, иначе пути по очереди обнуляли бы refcount друг друга. Загрузка динамики идёт до статики, потому что это объекты, на которые смотрит игрок: при голодном `--txd-budget` они получают ёмкость первыми, а статический мир честно деградирует в `deferred`/`remaining`. Байты под все динамические модели запрашиваются сразу, permanent-фейлы модели попадают в общий `knownFailures` и не перечитываются каждый кадр. Физики, гравитации, коллизий и AI пока нет; `--spawn N` в preview и `--spawn-objects N` в headless — детерминированные пробники слоя.

## Sector lists

`StaticWorld` раскладывает инстансы по оригинальной сетке `CWorld`: 100×100 секторов по 40 units, начало в -2000, то есть мир -2000..2000. Инстанс попадает в каждый сектор, которого касается его `WorldBounds`; только в middle-секторе (`xmid/ymid` по центру bounds) он лежит в main-листе, во всех остальных покрытых ячейках — в overlap-листе, как `CEntity::Add`. Инстансы, чьи bounds выходят за сетку, идут в `unindexed` и всегда попадают в выборку.

Запрос `QuerySectors(center, radius, results)` обходит только сектора вокруг точки и склеивает main+overlap; повторные попадания одного инстанса отсекаются scan code (`CEntity::m_scanCode`), поэтому разложение по нескольким секторам не даёт дублей ни в рендере, ни в collision-запросе. Это candidate set уровня секторов: distance, LOD и time-фильтры применяются вызывающим кодом, как в `CWorld`.

`QuerySectorsLinear` повторяет тот же предикат линейным сканом (сравнение по индексам ячеек, поэтому seam-границы не дают расхождений), и `--verify-sectors` сверяет множества (`missing`/`extra`) на нескольких кадрах, поэтому сломанная сетка не может тихо потерять геометрию. Оба запроса вызывают `EnsureCollisionBounds`, потому что COL может прийти после IPL и изменить bounds. World preview запрашивает кандидатов с запасом на streaming-радиус (1.5× view radius), чтобы prefetch-кольцо оставалось внутри одной выборки.

Draw order теперь следует порядку секторов, то есть зависит от позиции камеры: при переходе между секторами alpha-blended перекрытия могут «прыгать», потому что сортировки прозрачности в renderer'е нет. Набор геометрии от этого не меняется, но это отличие от прежнего IPL-порядка, а не просто другой порядок.

## Zones и path data

`ZoneWorld` хранит III/VC roots и исходные raw level IDs внутри выбранного profile. III `MAPZONE` заполняет отдельный map-level array; normal/navigation zones строят иерархию по алгоритму `CTheZones`. VC info/navigation/map arrays раздельны. Cull query объединяет флаги всех подходящих zones и берёт maximum wanted drop. VC occlusion сохраняет center после `z + height/2`, angle и quantized angle; это ещё не render-culling runtime.

`PathRecordParser` читает III text `ped|car` headers в IDE и VC numeric `0|1|2` headers в IPL. Каждая группа содержит ровно 12 nodes. `PathWorld` сохраняет порядок и привязывает attached groups к первому подходящему IPL instance; координаты вычисляются как `StoredPosition / 16` с object transform, detached groups используют world coordinates без instance. `--query-zone` и `--query-path GROUP NODE` проверяют результат на реальных данных.

Это typed data + world queries, не полный `CPathFind.PreparePathData`, connections/groups или AI. III на текущем дереве имеет 34 attached groups без соответствующего IPL instance; они явно выводятся как unresolved и не получают выдуманный transform. VC detached/water groups разрешаются независимо от models.

`PathGraph` (`GameDataLoader.PathGraph`) — подготовка path graph. Граф строится лениво и пересобирается, когда меняется `PathWorld.Version`, поэтому ссылки не могут указывать на уже сдвинутый transform. Тайл — это один bound instance (в оригинале `m_mapObjects`), поэтому модель, поставленная дважды, даёт два тайла с одинаковым `GroupIndex` и разными `TileIndex`. Узлом становится только slot с `type` 2 (`NodeTypeIntern`) — ровно то, что оригинал кладёт в `m_pathNodes`; `type` 1 (`NodeTypeExtern`) — граница тайла, `type` 0 — пустой padding. Не попавшие слоты считаются (`externalSlots`, `emptySlots`), чтобы суммы сходились с 12 слотами на тайл. Внутри тайла связь строится по двустороннему предикату оригинала `next[iseg] == jseg || next[jseg] == iseg` и пишется в оба списка, поэтому `Links` симметричны (`LinkCount` — число записей). Тайлы соединяются так же, как в оригинале: два extern-слота одного вида ближе 1 unit схлопываются в один переход `CrossTileLinkCount` между intern-узлами за ними, а непарные и неразрешимые extern'ы считаются (`unpairedExtern`, `unresolvedExtern`). `next`, указывающий на пустой или несуществующий слот, — `danglingNext`, на себя — `selfNext`; такие связи не выдумываются. `TryFindNearestNode` воспроизводит `CPathFind::FindNodeClosestToCoors`: перебор всех узлов вида, метрика `|dx| + |dy| + 3|dz|`, строгое `< distLimit` и исходный sentinel 10000. Компоненты связности помечаются как в `CountFloodFillGroups`: обход симметричных связей от первого непосещённого узла, отдельный счёт на вид, изолированный узел — компонента из одного узла; id доступен как `PathGraphNode.GroupId`, счётчики — `Path-Groups: components/car/ped/water/singles/largest`. Каждая связь несёт то, что оригинал хранит в `m_distances`/`m_connectionFlags`: `LinkDistance(i)` — евклидова длина, `LinkCrossesRoad(i)` — `bCrossesRoad`, который заполняется только для ped (у car-связей в оригинале lane-информация, а флаг остаётся пустым). Записей с флагом: III 512, VC 36. Не реализованы: флаги `m_connections` (`bCrossesRoad`, `bTrafficLight`), усреднённая lane direction и lane counts схлопнутой пары, `bDisabled`/`bBetweenLevels`, отдельный `DetachedInfoForTile*`-список (detached и attached extern'ы сливаются в одном списке) и route search — маршрут не прокладывается. Текущее дерево: III 2163 тайла, 4947 узлов (1971 car, 2976 ped), 7140 записей связей, 1634 перехода, 3457 непарных extern'ов и 1480 компонент (482 car / 998 ped, из них 936 одиночных, крупнейшая 254 узла); VC 1957 тайлов, 9642 узла (2973 car, 6291 ped, 378 water), 20350 записей, 2456 переходов, 178 непарных extern'ов и 42 компоненты (4 car / 32 ped / 6 water, без одиночных, крупнейшая 5102). III-граф фрагментирован, потому что половина extern'ов на этой карте не имеет пары в пределах 1 unit; VC сходится почти в одну компоненту.

Порог схлопывания extern-пар — предмет отдельного разбора. `--path-merge-distance R` пересобирает граф с другим радиусом; default 1 unit воспроизводит оригинал, всё остальное — задокументированный divergence. Замеры на III: 1 → 1480 компонент, 3 → 1414, 5 → 1356, 10 → 1018, 20 → 1437 (крупнейшая падает с 442 до 89 узлов), то есть радиус не является причиной фрагментации, а после ~10 unit greedy-слияние начинает склеивать чужие линии. Сравнение «полный transform против translation-only» дало почти одинаковые распределения расстояний, поэтому поворот инстансов тоже не виноват: extern-слоты III действительно не совпадают в пределах 1 unit. Дальнейший разбор требует сверки с тем набором тайлов, который видит оригинал, то есть статического реверса `gta3.exe` по `docs/REVERSE-ENGINEERING.md`, а не новых догадок.

## StaticWorld — осознанно узкий vertical slice

- IDE model metadata не содержит mesh/RW handles.
- IPL inst создаёт static placement с transform по `angle = -2*acos(w)` и stored axis. Scale сохраняется, но не применяется, как в исходном `LoadObjectInstance`.
- Sector grid 100×100, диапазон −2000…2000, sector 40. Объекты вне/на границе grid попадают в отдельный список.
- `QueryVertical` трансформирует world ray в local model space, выполняет sphere/box/triangle query и возвращает ближайший world hit. COLL lines хранятся, но в query не участвуют.
- Для VC до первого IPL все COLL entries из IMG читаются синхронно в slot order, как начальный `CColStore.LoadAllCollision`. Bounds-based streaming/eviction пока не реализованы.
- Это **не** dynamic `CWorld`, full line-of-sight, physics, LOD/streaming lifecycle или полный VC `CColStore`. Late COLL replacement обновляет broad phase, но не создаёт gameplay objects.

## Deterministic runtime

`SimulationRuntime` выполняет input → module list → audio service → presentation. Null services используются headless. Регистрация modules после старта запрещена, их порядок стабилен.

`SimulationClock` — 30 Hz: `DeltaSeconds=1/30`, GTA `TimeStep=50/30`. `GameClock` получает целые simulation milliseconds из tick index, поэтому не зависит от float wall clock. `StateWriter` записывает поля в явном порядке FNV-1a; имя бренда в hash не входит.

Оригинальный `CTimer` использует variable wall-clock delta и clamp. Compatibility timer должен быть добавлен отдельно, не подменяя deterministic test driver.

## Renderer-independent resources

`Rendering/` не знает Win32/D3D и не ссылается на native APIs. `DffRenderConverter` превращает static morph target + BinMesh в CPU vertices, triangle-list indices, material bindings и сохраняет `AtomicFrameIndex`. `DffFrameTransforms.Resolve` вычисляет static non-skinned `local * parent`; `DffRenderSceneBuilder` применяет placement и считает world bounds. `ModelRegistry` разрешает static LOD pair по оригинальному suffix-after-three правилу и сохраняет damage-aware near distance non-LOD модели; `StaticWorldScene` ограничивает placements радиусом камеры, считает draw distance `<2` невидимой и переключает detailed/LOD, не рисуя оба одновременно. `FreeFlyCamera` относится только к presentation и не создаёт player/physics state. `RenderResourceManager` кэширует DFF models и декодирует TXD level 0 в RGBA8 по запросу; persistent generic dictionaries являются fallback после model TXD, а missing textures не подменяются молча.

`RenderDrawCall` содержит geometry/mesh/material refs и transform. `NullRenderDevice` только валидирует snapshot и считает статистику, без окна/GPU. `--render-preview` загружает DFF, разрешает `PLAYER.TXD` из IMG, строит snapshot и проверяет весь pipeline на настоящих моделях. Это подготовка к D3D9, ещё не изображение и не игровой renderer.

## Отдельный D3D9 proof-of-concept

`SharpGTA.D3D9` — executable вне Core и Headless. `Win32Window` владеет Unicode window class, reverse-P/Invoke delegate и client size; mouse/keyboard events попадают в ограниченную очередь. `D3D9Device` использует только system `Direct3DCreate9` и direct COM vtable delegates с явным `StdCall`: HAL + software vertex processing, явно сообщаемый REF fallback, fixed-function `DrawPrimitiveUP` для triangle/UI и `DrawIndexedPrimitive` для static 3D meshes. `XYZRHW` vertices задаются в screen-space pixels; `D3dModelVertex` использует XYZ/normal/diffuse/UV, directional light и D24S8. `D3D9ModelMesh` создаёт managed-pool vertex/index buffers с оригинальными shared vertices/indices вместо expanded triangle arrays; stream/indices назначаются для каждого draw, а COM buffers освобождаются вместе с renderer. Managed A8R8G8B8 textures создаются с pitch-aware upload, переживают Reset и рисуются alpha-blended quads. Создаётся lockable back buffer; `ReadBackBufferBgra` учитывает pitch и вызывается до `Present`, затем `BmpWriter` сохраняет top-down 32-bit BMP. Cached surface освобождается до explicit `Reset`, COM interfaces/textures — при `Dispose`.

`WorldResourceManager` держит III models/TXD в двух `StreamingPool` и разрешает material сначала в IDE TXD своей модели (`GTA3.IMG`, затем sector-streamed `TXD.IMG`, затем loose `MODELS/*.TXD`), и только потом в persistent loose `MODELS/GENERIC.TXD`. Managed/GPU texture caches используют ключ `(IDE TXD, texture name)` и не смешивают одинаковые имена из разных словарей. Model draw distance и suffix LOD handoff ограничивают visible placements до запросов. Unload callback освобождает shared vertex/index buffers, texture context и CPU caches; DFF/TXD bytes приходят через `AsyncByteCache`, decode и register dictionaries остаются на simulation thread, а dynamic entity/time LOD отсутствует. Если unresolved LOD material всё же видим, он явно остаётся без texture вместо подмены чужим TXD. `WorldViewerApplication` закрывает дополнительный IMG catalog при `Dispose`.

Synthetic D3D9 tests фиксируют slots, HRESULT, layouts, imports и BMP без GPU/окна. `tools/test-d3d9.ps1` отдельно выполняет живой create/reset/draw/readback/present и pixel assertions на `net40` AnyCPU/x86 и `net9.0-windows`, плюс managed x86/x64 ABI tests. `tools/test-model.ps1` проверяет настоящий PLAYER.DFF/TXD через frame hierarchy, directional light, Z-buffer, BMP и Reset на трёх конфигурациях. `tools/test-world.ps1` дополнительно проверяет streamed static IPL/DFF/TXD resources, draw-distance/LOD scene, capacity block, async byte budget с eviction без потери кадра, generic/`TXD.IMG` lookup, BMP/Reset, живой `New Game → world`, free-fly input и COLL query. Static model/world preview уже подключены, но shaders/state cache, streaming/LOD/dynamic render passes и автоматический device-loss recovery ещё не реализованы.

## Launcher и III frontend

`src/SharpGTA.Core/Frontend` содержит независимые от GPU menu model, оригинальные III page layouts/hitboxes, GXT loader и таблицы CFont. `SharpGTA.Presentation/FrontendPreferencesState` — единый сериализованный state для launcher и menu; все настройки, включая brightness/volume/mouse, переживают round-trip. `D3D9FrontendRenderer` загружает MENU.TXD/FONTS.TXD в managed textures и рисует оригинальные backgrounds/logo/glyphs. `FrontendApplication` обрабатывает input, sliders, language/resolution/VSync и resize через Reset. New Game открывает явно названный static world preview с free-fly camera, не bootstrap игры. Load/save не имитируются: save profiles ещё отсутствуют, VC frontend не подменяется ресурсами III.

`SharpGTA.Launcher` — отдельный WinForms `WebBrowser`/MSHTML host с embedded HTML/CSS/ES5. CSS не использует custom properties/grid/flex; никаких WebView2, CDN или внешних DLL. JS вызывает host через отменяемую `sharpgta://` навигацию, payload забирается `InvokeScript`, ответы приходят `sharpGtaReceive`. Launcher стартует D3D9 напрямую, сохраняет state в LocalAppData или portable `<EXE>/user`, а оригинальные ресурсы не меняет. `tools/test-launcher.ps1` проверяет реальный DOM/bridge, запуск frontend и settings reload; отладочный профиль изолирован.

## Что находится за границей текущего кода

- Animation/skinning runtime, D3D8 pre-instanced geometry, PAL4/DXT2/DXT4 и PS2-specific asset decoders;
- IFP/SCM, full paths/zones/timecycle/handling/weapons configs;
- byte-memory eviction tuning для нескольких каналов, полный `CStreaming` lifecycle и gameplay streaming;
- gameplay entities (peds/vehicles/objects) и dynamic LOD, полный CPhysical/Collision/CWorld, weapons/AI;
- gameplay camera/HUD/audio, полный игровой renderer/saves и frontend за пределами III;
- VC frontend, gameplay bootstrap/save profiles и полный D3D9 streaming/LOD/dynamic renderer.

Эти функции не считаются реализованными из-за наличия DTO, chunk inventory или null interfaces.
