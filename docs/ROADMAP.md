# Дорожная карта и честный статус

Легенда: `[x]` работает и покрыто synthetic tests; `[~]` есть узкая основа, но не полная совместимость; `[ ]` не реализовано. Наличие исходного референса не отмечает пункт как выполненный.

## Этап 0 — воспроизводимый managed/headless фундамент

- [x] SDK solution, C# 4, `net40` shipping + `net9.0` test target, без сторонних runtime dependencies; только compile-only targeting pack.
- [x] Единый брендинг через `Directory.Build.props` и generated `Branding.g.cs`.
- [x] Git init, root `refs/` в `.gitignore`, локальный Git author `SharpGTA Agent`.
- [x] Root-scoped read-only FS, invariant tokenizer, little-endian bounds-checked readers.
- [x] Managed Vector2/Vector3/Quaternion/Matrix/bounds и PC CodeWarrior RNG.
- [x] Независимые III/VC definitions, limits, data schemas.
- [x] Deterministic 30 Hz runtime, null input/audio/presentation, game clock, `State@N`.
- [x] Собственный test runner без библиотек, synthetic tests на двух runtimes.

## Этап 1 — asset/content vertical slice

- [x] IMG/DIR directory, multi-image catalog, streaming reads без загрузки всего IMG.
- [x] III/VC GXT main TKEY/TDAT и VC TABL mission tables, raw code units.
- [~] DAT/IDE/IPL: порядок и основные objs/tobj/cars/peds/weap/inst; zones/cull/occl/paths typed, 2dfx/pick пока raw.
- [x] Legacy COLL reader, sphere/box/triangle local queries, vertical static ground query.
- [x] Model registry + static IPL world + sector broad phase + bounded diagnostics.
- [x] RenderWare chunk inventory и static non-native DFF: Clump/FrameList/GeometryList/Atomic, geometry/morph, materials/texture refs, BinMesh list/strip, multi-clump и stale parent lengths.
- [x] DFF HAnim, old/split Skin и MatFX (включая embedded textures) читаются на всех реальных DFF; counters в `--dff[-entry]/scan`.
- [~] Animation/skinning/render behavior и D3D8 pre-instanced geometry ещё не перенесены.
- [x] Smoke `LoadStandard()` на двух локальных реальных III/VC trees, обоих runtimes; counts в `docs/REAL-ASSETS.md`.
- [x] Начальная синхронная загрузка 30 VC COLL-ресурсов из IMG до первого IPL и реальные ground queries.
- [ ] Расширенные golden manifests/запросы для retail variants; bounds-based streaming/eviction ещё впереди.
- [x] Typed zones/cull/occlusion и IDE/IPL path groups с III/VC layouts, world transforms и `--query-zone`/`--query-path`; полный CPathFind/AI не реализован.
- [x] Static model draw distance и suffix-after-three LOD pair (`LODerind02` ↔ `dinerind02`) с handoff по non-LOD near distance; timed (`tobj`) visibility по `GameClock` работает, dynamic/entity LOD не реализован.
- [ ] Typed 2dfx/pick, handling/object/surface/pedstats/weapon/timecycle/water configs и полный path graph preparation.
- [x] Полное чтение всех nonempty DFF из локальных IMG: III 3134 + 4 empty placeholders; VC 4617; ноль malformed entries.
- [x] PC D3D8 TXD dictionaries, palettes, mip levels и RGBA decode реальных uncompressed/DXT1/DXT3 форматов; полный IMG scan обеих игр проходит. DXT5 покрыт synthetic tests.
- [ ] Оставшиеся DFF plugins/native branches и специальные PAL4/DXT2/DXT4/PS2 asset decoders, если появятся соответствующие samples/потребность.
- [ ] IFP animations и SCM VM с отдельными numeric opcode tables III/VC.

## Этап 2 — streaming и simulation world

- [~] Model/TXD streaming slots: states/refcount/priority/Deferred, sync `LoadAllRequested`, visible-scene requests, LRU eviction и CPU/GPU unload hooks; полный `CStreaming` lifecycle и channel states ещё нет.
- [~] Два async I/O channel (`AsyncByteCache`, 2 background threads) доставляют только bytes для DFF/TXD с bounded byte LRU (`--byte-budget`); decode/binding/mutation остаются на simulation thread, prefetch ring работает. IDE/IPL/COL/IMG loading и gameplay streaming пока синхронны.
- [ ] III level collision loading и VC slot-based streamed collision как отдельные modules.
- [~] Entity pools/handles, sector lists, buildings/objects/dummies, LOD/time visibility. Готовы fixed `EntityPool` с оригинальным handle/generation encoding, `GameClock`-driven `TimeObjectController`, `--time`, оригинальная сетка `CWorld` (100×100×40, main/overlap, scan codes, `QuerySectors` + linear-verify), `OBJECT.DAT` и классификация placements в building/dummy/treadable пулы с handle'ами (`--query-building`), `WorldEntity`-база и динамические objects/peds/vehicles с пулами, секторной регистрацией, линейным движением, детерминированным `DynamicEntityModule` (`--spawn-objects`, state hash) и рендером в D3D9 preview (`--spawn`). Физики/коллизий, AI и анимации динамических entities ещё нет.
- [ ] Полный CPhysical, collision response, moving entities, vertical line/LOS, dynamic objects.
- [~] Path graph preparation, population/car generators, zones/interiors. Готов `PathGraph`: тайл на каждый bound instance (как `m_mapObjects`), узлы только `type` 2 (intern), симметричные связи по двустороннему предикату `next`, схлопывание extern-пар в пределах 1 unit в межтайловые связи, flood-fill компоненты (`GroupId`, как `CountFloodFillGroups`), счётчики extern/пустых/битых/непарных слотов, `TryFindNearestNode` с оригинальной метрикой `|dx|+|dy|+3|dz|` и строгим лимитом, диагностика `Path-Graph:`/`Path-Groups:`/`Path-Nearest:` и `--query-path-nearest`. `m_connections`-флаги, lane direction/counts, route search и сама population/генераторы ещё нет. `--path-merge-distance R` даёт измерять чувствительность графа к порогу схлопывания extern-пар (default 1 — оригинал).
- [ ] Canonical world snapshot, stable iteration/IDs, replay и divergence dumps.
- [ ] Wall-clock compatibility timer в дополнение к deterministic driver; replay/pause profiles.

## Этап 3 — gameplay III/VC

- [ ] Player/ped AI, animation state, weapons, vehicles и VC bikes/boats/flying.
- [ ] Camera, game logic, missions/scripts, pickups/garages/restart/weather/water.
- [ ] III MLO/subway/cull-zone specifics; VC interiors/ropes/attractors/set pieces/stinger и др.
- [ ] Отдельные save schemas III/VC и retail compatibility tests; не копировать C++ pointers/struct images.
- [ ] Полный gameplay parity scenarios на оригинальных данных, не только smoke tests.

## Этап 4 — Windows XP presentation

- [~] Win32 window/input host: Unicode окно, message loop и mouse/keyboard input работают в III frontend; общий presentation host ещё не завершён. Headless не использует window device.
- [x] Managed mesh/material/texture DTO, DFF converter, lazy texture cache и NullRenderer с `--render-preview`; реальные PLAYER модели проходят без native APIs.
- [~] D3D9 COM interop без D3DX/D3DCompiler: vtable/HRESULT/layout/import tests, fixed-function triangle/3D meshes, managed vertex/index buffers и `DrawIndexedPrimitive`, managed A8R8G8B8 textures, alpha-blended quads/glyphs, directional light и D24S8; shaders/state cache ещё нет.
- [~] Explicit Reset, COM/surface ownership, managed x86/x64 ABI tests и живой D3D9 triangle/readback/present smoke проходят; автоматическое device-loss recovery и полный игровой renderer ещё впереди.
- [~] GTA frontend: MSHTML Launcher + III start/options/exit, мышь/клавиатура, настройки, разрешение и 5 языков работают на оригинальных TXD/GXT; VC frontend, saves и gameplay bootstrap ещё не подключены. III static DFF model preview и streamed static IPL world preview с free-fly/COLL query, sector-list candidates, draw-distance/LOD handoff, timed-object `GameClock` visibility, bounded LRU pools и async DFF/TXD byte delivery работают, но dynamic peds/vehicles/HUD passes, полный уровень streaming и полный renderer не реализованы.
- [ ] Audio decoding/mixing и XP-compatible backend, NullAudio для simulation tests.
- [ ] Installer/packaging без сторонних DLL, оригинальные assets остаются у пользователя.

## Этап 5 — hardening и расширения

- [ ] Windows XP SP3 x86 + .NET Framework 4.0 live VM; текущие проверки выполняются на современной Windows/.NET 4.8 и не заменяют XP.
- [ ] Real-asset long soak, malformed/truncated fuzz, low-memory и slow-I/O tests.
- [ ] Multiplayer по [IDEAS.md](../IDEAS.md): snapshots/server authority/dedicated, затем late join/prediction/master/relay.
- [ ] Межигровая загрузка ресурсов по [RESOURCE-COMPATIBILITY.md](RESOURCE-COMPATIBILITY.md): semantic manifests, shared packs, staging/rollback, offline map conversion и запрет слепого смешивания raw IDs/SCM/saves.
- [ ] GTA SA только после стабильных III/VC и пересмотра готовности референсов; сейчас не начат.

## Следующий конкретный шаг

MSHTML Launcher, III frontend, DFF model preview и streamed static IPL world preview уже работают с оригинальными ресурсами, draw-distance/LOD handoff, timed-object clock visibility, async DFF/TXD byte delivery, free-fly/COLL query и D3D9 Reset. Следующий streaming-шаг — sector lists и gameplay entity pools (peds/vehicles/objects), затем gameplay bootstrap. Параллельно остаются VC frontend profile и полный path graph preparation.
