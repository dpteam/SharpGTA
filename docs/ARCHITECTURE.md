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

## Zones и path data

`ZoneWorld` хранит III/VC roots и исходные raw level IDs внутри выбранного profile. III `MAPZONE` заполняет отдельный map-level array; normal/navigation zones строят иерархию по алгоритму `CTheZones`. VC info/navigation/map arrays раздельны. Cull query объединяет флаги всех подходящих zones и берёт maximum wanted drop. VC occlusion сохраняет center после `z + height/2`, angle и quantized angle; это ещё не render-culling runtime.

`PathRecordParser` читает III text `ped|car` headers в IDE и VC numeric `0|1|2` headers в IPL. Каждая группа содержит ровно 12 nodes. `PathWorld` сохраняет порядок и привязывает attached groups к первому подходящему IPL instance; координаты вычисляются как `StoredPosition / 16` с object transform, detached groups используют world coordinates без instance. `--query-zone` и `--query-path GROUP NODE` проверяют результат на реальных данных.

Это typed data + world queries, не полный `CPathFind.PreparePathData`, connections/groups или AI. III на текущем дереве имеет 34 attached groups без соответствующего IPL instance; они явно выводятся как unresolved и не получают выдуманный transform. VC detached/water groups разрешаются независимо от models.

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

`Rendering/` не знает Win32/D3D и не ссылается на native APIs. `DffRenderConverter` превращает static morph target + BinMesh в CPU vertices, triangle-list indices, material bindings и сохраняет `AtomicFrameIndex`. `DffFrameTransforms.Resolve` вычисляет static non-skinned `local * parent`; `DffRenderSceneBuilder` применяет placement и считает world bounds; `StaticWorldScene` ограничивает placements радиусом камеры, сохраняя их atomic transforms. `FreeFlyCamera` относится только к presentation и не создаёт player/physics state. `RenderResourceManager` кэширует DFF models и декодирует TXD level 0 в RGBA8 по запросу; persistent generic dictionaries являются fallback после model TXD, а missing textures не подменяются молча.

`RenderDrawCall` содержит geometry/mesh/material refs и transform. `NullRenderDevice` только валидирует snapshot и считает статистику, без окна/GPU. `--render-preview` загружает DFF, разрешает `PLAYER.TXD` из IMG, строит snapshot и проверяет весь pipeline на настоящих моделях. Это подготовка к D3D9, ещё не изображение и не игровой renderer.

## Отдельный D3D9 proof-of-concept

`SharpGTA.D3D9` — executable вне Core и Headless. `Win32Window` владеет Unicode window class, reverse-P/Invoke delegate и client size; mouse/keyboard events попадают в ограниченную очередь. `D3D9Device` использует только system `Direct3DCreate9` и direct COM vtable delegates с явным `StdCall`: HAL + software vertex processing, явно сообщаемый REF fallback, fixed-function `DrawPrimitiveUP`. `XYZRHW` vertices задаются в screen-space pixels; `D3dModelVertex` использует XYZ/normal/diffuse/UV, directional light и D24S8 для static 3D meshes. Managed A8R8G8B8 textures создаются с pitch-aware upload, переживают Reset и рисуются alpha-blended quads. Создаётся lockable back buffer; `ReadBackBufferBgra` учитывает pitch и вызывается до `Present`, затем `BmpWriter` сохраняет top-down 32-bit BMP. Cached surface освобождается до explicit `Reset`, COM interfaces/textures — при `Dispose`.

`WorldResourceManager` лениво разрешает III IPL models и их TXD: material сначала ищется в IDE TXD своей модели (`GTA3.IMG`, затем sector-streamed `TXD.IMG`, затем loose `MODELS/*.TXD`), и только потом в persistent loose `MODELS/GENERIC.TXD`. Managed/GPU texture caches используют ключ `(IDE TXD, texture name)` и не смешивают одинаковые имена из разных словарей. World preview отбирает static placements вокруг камеры и использует те же DFF frame transforms; это bounded lazy cache, не полный GTA streaming/LOD lifecycle. Например, unresolved `LODerind02 → indust1/newall6_16c128` явно остаётся без texture вместо подмены чужим TXD. `WorldViewerApplication` закрывает дополнительный IMG catalog при `Dispose`.

Synthetic D3D9 tests фиксируют slots, HRESULT, layouts, imports и BMP без GPU/окна. `tools/test-d3d9.ps1` отдельно выполняет живой create/reset/draw/readback/present и pixel assertions на `net40` AnyCPU/x86 и `net9.0-windows`, плюс managed x86/x64 ABI tests. `tools/test-model.ps1` проверяет настоящий PLAYER.DFF/TXD через frame hierarchy, directional light, Z-buffer, BMP и Reset на трёх конфигурациях. `tools/test-world.ps1` дополнительно проверяет static IPL/DFF/TXD resources, generic/`TXD.IMG` lookup, BMP/Reset, живой `New Game → world`, free-fly input и COLL query. Static model/world preview уже подключены, но vertex buffers/shaders/state cache, streaming/LOD/dynamic render passes и автоматический device-loss recovery ещё не реализованы.

## Launcher и III frontend

`src/SharpGTA.Core/Frontend` содержит независимые от GPU menu model, оригинальные III page layouts/hitboxes, GXT loader и таблицы CFont. `SharpGTA.Presentation/FrontendPreferencesState` — единый сериализованный state для launcher и menu; все настройки, включая brightness/volume/mouse, переживают round-trip. `D3D9FrontendRenderer` загружает MENU.TXD/FONTS.TXD в managed textures и рисует оригинальные backgrounds/logo/glyphs. `FrontendApplication` обрабатывает input, sliders, language/resolution/VSync и resize через Reset. New Game открывает явно названный static world preview с free-fly camera, не bootstrap игры. Load/save не имитируются: save profiles ещё отсутствуют, VC frontend не подменяется ресурсами III.

`SharpGTA.Launcher` — отдельный WinForms `WebBrowser`/MSHTML host с embedded HTML/CSS/ES5. CSS не использует custom properties/grid/flex; никаких WebView2, CDN или внешних DLL. JS вызывает host через отменяемую `sharpgta://` навигацию, payload забирается `InvokeScript`, ответы приходят `sharpGtaReceive`. Launcher стартует D3D9 напрямую, сохраняет state в LocalAppData или portable `<EXE>/user`, а оригинальные ресурсы не меняет. `tools/test-launcher.ps1` проверяет реальный DOM/bridge, запуск frontend и settings reload; отладочный профиль изолирован.

## Что находится за границей текущего кода

- Animation/skinning runtime, D3D8 pre-instanced geometry, PAL4/DXT2/DXT4 и PS2-specific asset decoders;
- IFP/SCM, full paths/zones/timecycle/handling/weapons configs;
- model request/refcount/streaming channels и async I/O;
- entities/pools, full CPhysical/Collision/CWorld, peds/vehicles/weapons/AI;
- gameplay camera/HUD/audio, полный игровой renderer/saves и frontend за пределами III;
- VC frontend, gameplay bootstrap/save profiles и полный D3D9 streaming/LOD/dynamic renderer.

Эти функции не считаются реализованными из-за наличия DTO, chunk inventory или null interfaces.
