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
- [ ] Typed 2dfx/pick, handling/object/surface/pedstats/weapon/timecycle/water configs и полный path graph preparation.
- [x] Полное чтение всех nonempty DFF из локальных IMG: III 3134 + 4 empty placeholders; VC 4617; ноль malformed entries.
- [x] PC D3D8 TXD dictionaries, palettes, mip levels и RGBA decode реальных uncompressed/DXT1/DXT3 форматов; полный IMG scan обеих игр проходит. DXT5 покрыт synthetic tests.
- [ ] Оставшиеся DFF plugins/native branches и специальные PAL4/DXT2/DXT4/PS2 asset decoders, если появятся соответствующие samples/потребность.
- [ ] IFP animations и SCM VM с отдельными numeric opcode tables III/VC.

## Этап 2 — streaming и simulation world

- [ ] Model/TXD request states, refcount/LRU, sync `LoadAllRequestedModels`, scene loading.
- [ ] Два async I/O channels только для доставки bytes; decode/binding/mutation на simulation thread.
- [ ] III level collision loading и VC slot-based streamed collision как отдельные modules.
- [ ] Entity pools/handles, sector lists, buildings/objects/dummies, LOD/time visibility.
- [ ] Полный CPhysical, collision response, moving entities, vertical line/LOS, dynamic objects.
- [ ] Path graph preparation, population/car generators, zones/interiors.
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
- [~] D3D9 COM interop без D3DX/D3DCompiler: vtable/HRESULT/layout/import tests, fixed-function triangle, managed A8R8G8B8 textures и alpha-blended quads/glyphs; vertex buffers/shaders/state cache ещё нет.
- [~] Explicit Reset, COM/surface ownership, managed x86/x64 ABI tests и живой D3D9 triangle/readback/present smoke проходят; автоматическое device-loss recovery и полный игровой renderer ещё впереди.
- [~] GTA frontend: MSHTML Launcher + III start/options/exit, мышь/клавиатура, настройки, разрешение и 5 языков работают на оригинальных TXD/GXT; VC frontend, saves и gameplay bootstrap ещё не подключены. World/models/peds/vehicles/HUD render passes не реализованы.
- [ ] Audio decoding/mixing и XP-compatible backend, NullAudio для simulation tests.
- [ ] Installer/packaging без сторонних DLL, оригинальные assets остаются у пользователя.

## Этап 5 — hardening и расширения

- [ ] Windows XP SP3 x86 + .NET Framework 4.0 live VM; текущие проверки выполняются на современной Windows/.NET 4.8 и не заменяют XP.
- [ ] Real-asset long soak, malformed/truncated fuzz, low-memory и slow-I/O tests.
- [ ] Multiplayer по [IDEAS.md](../IDEAS.md): snapshots/server authority/dedicated, затем late join/prediction/master/relay.
- [ ] Межигровая загрузка ресурсов по [RESOURCE-COMPATIBILITY.md](RESOURCE-COMPATIBILITY.md): semantic manifests, shared packs, staging/rollback, offline map conversion и запрет слепого смешивания raw IDs/SCM/saves.
- [ ] GTA SA только после стабильных III/VC и пересмотра готовности референсов; сейчас не начат.

## Следующий конкретный шаг

MSHTML Launcher и III frontend уже работают с оригинальными ресурсами, мышью/клавиатурой, настройками и D3D9 Reset. Следующий presentation-шаг — подключить managed mesh/texture DTO через vertex buffers и textures, затем первый реальный DFF render; отдельно добавить VC frontend profile и gameplay bootstrap. Параллельный content-шаг — полный path graph preparation.
