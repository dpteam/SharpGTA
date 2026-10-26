# Архитектура текущего фундамента

## Зависимости

```text
SharpGTA.Core
  ↑             ↑
Games.III     Games.VC
  ↑             ↑
     SharpGTA.Headless
             ↑
       SharpGTA.Tests
```

Core не знает конкретные игровые классы, Win32, Direct3D или RenderWare objects. Headless выбирает `IIIDefinition.Instance`/`VCDefinition.Instance` и собирает runtime. Собственные DLL проекта не являются сторонними зависимостями; shipping состоит только из этих сборок и .NET Framework/system API.

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

`DffReader` создаёт CPU-side DTO без GPU: Clump/frames/GeometryList/Atomics, platform-independent geometry, morph targets, materials и texture references, BinMesh list/strip. Поддержаны legacy embedded Atomic, multi-clump files и известные retail Clump parent-length mismatches через count-driven parsing. Unknown extensions пропускаются по declared length; native geometry не выдаётся за static mesh. `--dff-scan` декодирует каждый nonempty DFF entry без удержания всего набора в памяти.

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

## Что находится за границей текущего кода

- Skin/HAnim/MatFX, D3D8 pre-instanced geometry и TXD native pixels/mipmaps;
- IFP/SCM, full paths/zones/timecycle/handling/weapons configs;
- model request/refcount/streaming channels и async I/O;
- entities/pools, full CPhysical/Collision/CWorld, peds/vehicles/weapons/AI;
- camera/frontend/HUD/audio/render/saves;
- Win32 host и D3D9 COM backend.

Эти функции не считаются реализованными из-за наличия DTO, chunk inventory или null interfaces.
