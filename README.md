# SharpGTA

[![CI](https://github.com/dpteam/SharpGTA/actions/workflows/ci.yml/badge.svg)](https://github.com/dpteam/SharpGTA/actions/workflows/ci.yml)

Управляемый движок на C# для данных и игровой логики **GTA III** и **GTA Vice City**. Целевая поставка — **.NET Framework 4.0 / Windows XP SP3**, без сторонних runtime-библиотек. Исходные игровые ресурсы не входят в репозиторий и должны принадлежать пользователю.

> **Статус:** это рабочий headless-фундамент, а не playable-порт. Уже работают профили III/VC, DAT/IDE/IPL/COLL/IMG/GXT, static non-native DFF с geometry/materials/BinMesh, D3D8 TXD с mipmaps и RGBA decode, HAnim/Skin/MatFX readers, IFP-пакеты анимаций (только чтение), статические IPL-объекты и COLL-запросы. Animation runtime, SCM, динамической физики, пешеходов, транспорта, полного игрового renderer и звука пока нет; уже есть MSHTML Launcher, интерактивное меню GTA III и streamed static world preview с draw-distance/LOD на оригинальных данных. См. [дорожную карту](docs/ROADMAP.md).

## Сборка

Нужен .NET SDK 9.0.318 или совместимый SDK из `global.json`. Для компиляции `net40` используется `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 — **только build-time targeting pack**, аналогично DPTDOOM2. Его DLL не попадают в поставку; сторонних runtime-библиотек и внешних native DLL нет. Restore использует стандартный источник NuGet, если targeting pack ещё не находится в локальном кэше.

```powershell
dotnet build SharpGTA.sln -c Release
powershell -NoProfile -File tools/test.ps1
# На Windows с рабочим D3D9: managed x86/x64 ABI + живой GPU smoke.
powershell -NoProfile -File tools/test-d3d9.ps1
# Если оригинальные деревья лежат в refs/, дополнительно:
powershell -NoProfile -File tools/test-model.ps1
powershell -NoProfile -File tools/test-world.ps1
powershell -NoProfile -File tools/test-real-assets.ps1
```

Поставляемая конфигурация — `net40`. `net9.0` / `net9.0-windows` существуют только для быстрого запуска тех же исходников и тестов на современном runtime. Зелёный `net9.0` не считается доказательством совместимости с XP.

## GitHub Actions и артефакты

CI на `windows-latest` собирает `net40` и `net9.0`, запускает все tests и static checks, а затем публикует артефакты на 30 дней:

- **`SharpGTA-net40-Windows`** — готовый EXE/DLL/ZIP для скачивания через **Actions → run → Artifacts**;
- **`SharpGTA-Source`** — исходники и документация.

Оригинальные игровые ресурсы в артефакты не входят; на чистом CI real-asset suite корректно помечается `SKIP`. Подробности — в [docs/CI.md](docs/CI.md).

## Headless

Быстрый прогон без игровых файлов, GPU, окна и звука:

```powershell
src\SharpGTA.Headless\bin\Release\net40\SharpGTA.Headless.exe --game iii --ticks 90 --state-at 30
src\SharpGTA.Headless\bin\Release\net40\SharpGTA.Headless.exe --game vc --ticks 30
```

Загрузка оригинальных данных:

```powershell
# --root без других asset-команд подразумевает --standard:
# MODELS/GTA3.IMG + DIR, DATA/DEFAULT.DAT, затем DATA/GTA3.DAT или GTA_VC.DAT.
SharpGTA.Headless.exe --game iii --root "D:\Games\GTA III"
SharpGTA.Headless.exe --game vc --root "D:\Games\GTA Vice City" --verbose

# Отдельные проверки.
SharpGTA.Headless.exe --root "D:\Games\GTA III" --standard --query-ground 10 20 -5 100
SharpGTA.Headless.exe --root "D:\Games\GTA III" --standard --query-zone 812 -936 36 --query-path 0 0 --query-path-nearest 812 -936 36
SharpGTA.Headless.exe --root "D:\Games\GTA III" --gxt TEXT/AMERICAN.GXT --gxt-key MAIN
SharpGTA.Headless.exe --root "D:\Games\GTA III" --standard --rw-entry PLAYER.DFF
SharpGTA.Headless.exe --root "D:\Games\GTA III" --dff-entry PLAYER.DFF
SharpGTA.Headless.exe --root "D:\Games\GTA III" --dff-scan
SharpGTA.Headless.exe --root "D:\Games\GTA III" --txd-entry PLAYER.TXD
SharpGTA.Headless.exe --root "D:\Games\GTA III" --txd-scan
SharpGTA.Headless.exe --root "D:\Games\GTA III" --ifp ANIM/ped.ifp --ifp-animation walk_civi
SharpGTA.Headless.exe --root "D:\Games\GTA III" --img MODELS/GTA3.IMG --dff-entry PLAYER.DFF --render-preview
SharpGTA.Headless.exe --root "D:\Games\GTA III" --dat DATA/CUSTOM.DAT --strict
```

`--rw`/`--rw-entry` показывают только проверенную по границам **инвентаризацию RenderWare chunks**. `--dff`/`--dff-entry` декодируют static non-native geometry/materials/BinMesh и HAnim/Skin/MatFX, а `--dff-scan` проверяет все DFF entries и отдельно считает empty/malformed и plugin counts. `--txd`/`--txd-entry` читают D3D8 dictionaries и декодируют все mip levels в RGBA; `--txd-scan` проверяет каждую TXD entry и все пиксели, отдельно считая unsupported PS2 rasters. `--ifp` читает IFP-пакеты анимаций (сопряжение кватернионов, абсолютные времена → дельты, снятие flips), а `--ifp-animation NAME` печатает одну иерархию по имени; скелет и playback ещё не подключены. `--render-preview` дополнительно проверяет managed mesh/material/texture pipeline через NullRenderer, по-прежнему без GPU/окна. `--state-at N` печатает диагностический FNV-1a-хеш состояния; это не криптографический хеш и не сетевой античит. Полный список опций: `--help`.

## Launcher и меню GTA III

Самый простой запуск:

```powershell
src\SharpGTA.Launcher\bin\Release\net40\SharpGTA.Launcher.exe
```

Launcher использует системный **MSHTML** (`WebBrowser`), embedded HTML/CSS/ES5 и `sharpgta://` bridge — без WebView2, CDN, HTTP server и внешних DLL. В нём можно выбрать папку игры, язык, разрешение, VSync/frame limiter и тему. Кнопка запуска открывает D3D9 frontend; состояние сохраняется в `%LOCALAPPDATA%\SharpGTA\launcher.ini` либо в `<EXE>/user/`, если рядом есть `portable.txt`.

Внутри меню работают мышь, стрелки, Enter, Esc и Left/Right для настроек. Используются оригинальные `MENU.TXD`, `FONTS.TXD` и GXT. Доступны Start Game/Options/Exit, подтверждения, слайдеры, переключатели, пять языков и смена разрешения. `New Game → Yes` открывает **интерактивный static world preview GTA III**, а не gameplay; save/load по-прежнему сообщает о неподключённых профилях. Vice City выбирается в Launcher как профиль данных, но её интерактивное меню ещё не реализовано и не подменяется III.

Прямой запуск static world preview:

```powershell
src\SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe --world --game iii --root "D:\Games\GTA III"
```

Viewer строит оригинальные IPL placements, применяет model draw distance и оригинальный suffix LOD handoff, затем streamed DFF/TXD slots из `GTA3.IMG`/`TXD.IMG`. Каждый placement получает handle в пуле своего вида (`building`/`dummy`/`treadable`) по тем же правилам, что оригинал: `OBJECT.DAT`-модели → dummy, path-модели → treadable, остальные → building; `--query-building HANDLE` в headless резолвит handle обратно в placement, а stale handle честно отвечает `stale=true`.

Gameplay entities (`WorldEntity` → `DynamicEntity`) уже живут в тех же пулах и том же секторном гриде: `--spawn N` создаёт objects перед камерой, `DynamicEntityModule` двигает их линейно и переносит между секторами детерминированно, а D3D9 preview рисует их теми же streamed model/TXD slots (диагностика `World-Dynamic: spawned/alive/drawn/sectorMoves`). `--spawn-cars N` и `--spawn-peds N` ставят actors на сети путей: и машины, и пешеходы ведёт один `PathFollowerModule` (car-узлы для машин, ped-узлы для пешеходов), он же владеет их позицией и перепланирует маршрут в конце, поэтому `--spawn-objects` рядом с ними не создаёт второго интегратора. Модель по умолчанию — первый реальный vehicle/ped рекорд: `null`-модель движка (id 0) пропускается, иначе actor ходил бы и считался живым, но никогда не рисовался. Ped DFF в III состоит из двух clump'ов (тело и голова), поэтому render path конвертирует все clump'ы документа, а не только первый. С `--recycle` включается `TrafficPopulationModule`: actor, закончивший маршрут, деспавнится из мира и заменяется новым на другом узле, так что население держится на заданном числе (`Path-Traffic: cars=live/target ... spawned/despawned/recycled`). В headless у актёров маршрут в 40 hops (до ~2800 unit в III), в D3D9 preview — 6 hops, иначе за короткий прогон ни одна замена не успевает случиться. Машина при этом едет по полосе: `CarLanes` берёт `LANE_WIDTH = 5` и `CCarPathLink::OneWayLaneOffset`, смещает актёра в сторону движения, поэтому встречные машины расходятся по разным сторонам линии; полоса выбирается стабильно по handle, без случайных перестроений, и на реальных данных полос бывает до 2 (III) и до 3 (VC) в каждую сторону (`Path-Lanes`). На светофорах машины останавливаются: `Path-Lights` показывает, сколько размещённых светофоров реально пометило связи (III 333 → 313 связей, VC 271 → 271), фазы (`LightForCars1/2`, 16 с) идут по симуляционному времени, а `holds=` в `Path-Follow`/`World-Actors-Final` считает actor-тики ожидания. За светофором образуется очередь: `VehicleGap` (6 unit) держит зазор до лидера в той же полосе, потому что настоящих коллизий (`CPhysical`) в движке ещё нет. Пешеходы тоже ждут: светофоры помечают переходы с `crossesRoad` (`pedLinks` в `Path-Lights`; III 402, VC 20), фаза `PhasePeds` даёт 12 с «не идти» / 5 с «идти» / 1 с мигания, и пешеход стоит на узле перед переходом, но не замирает посреди дороги.

Первый кусок «играбельного» движения — `CharacterMoveModule`: `--walk X Y Z DX DY SPEED` спавнит одного педа и ведёт его по вводу, а `StaticWorld.QueryRay` (луч по COLL) плюс три зонда на высоте груди не дают пройти сквозь стену и заставляют скользить вдоль неё; вертикаль повторяет `CPhysical::ApplyGravity` (`GRAVITY = 0.008`), поэтому шаг с края — это настоящее падение с приземлением, а не телепорт вниз; тело — два кольца зондов (0.45 и грудь), так что бордюр перешагивается, а низкая стенка 0.6 блокирует. Другие актёры — цилиндры (`ActorLimit`): в педа или машину персонаж упрётся, но не толкнёт их — реакция односторонняя, общего решателя ещё нет. То же самое в preview под флагом `--play`: WASD задаёт направление относительно камеры, камера третьего лица висит за спиной и укорачивает вынос, если сзади стена (RMB — орбита). Это ещё не `CPhysical`: нет step/slope response, столкновений между entity и анимации. Физики, коллизий, AI, анимации и traffic rules у актёров пока нет — это следующий шаг, а не скрытая возможность.

Инстансы разложены по оригинальной сетке `CWorld` (100×100 секторов по 40 units), кадр берёт кандидатов через `QuerySectors` вместо полного скана мира; `--verify-sectors` сверяет выборку с linear scan и печатает `missing/extra`. Материалы используют IDE TXD своей модели, затем `GENERIC.TXD` как fallback; чужие одноимённые textures не подставляются. Slots имеют refcount/приоритет/LRU/Deferred, а `--stream-budget N`/`--txd-budget N` задают capacities моделей/TXD (defaults 512/96). Нехватка capacity явно оставляет requests в queue и не превращает их в permanent failed models; near LOD используется как fallback, если detailed model недоступен.

DFF/TXD bytes читают два фоновых канала `IO/AsyncByteCache` с bounded byte LRU (`--byte-budget N` MiB, default 64) и prefetch-кольцом; decode и любая mutation мира выполняются на simulation thread. Первый кадр ждёт доставку, поэтому startup детерминирован, а дальше новый контент может появиться на кадр позже — это честный streaming lag, а не дыра в сцене.

Timed-объекты IDE (`tobj`) живут в `EntityPool` и создаются/освобождаются по игровым часам: `--time H` задаёт стартовый час (default 12), дальше `Runtime.GameClock` идёт детерминированным 30 Hz driver'ом — одна игровая минута на 30 кадров, не по wall clock. Окно видимости — часы с эксклюзивным концом и переходом через полночь. `_nt`/`_dy` пары резолвятся по имени, как в оригинале. В районе спавна ни одна timed-модель не попадает в радиус preview, поэтому day/night проба проверяет жизненный цикл пула, а не пиксели.

Работают `WASD` для движения, `Q/E` для высоты, обзор с RMB, `F` для возврата к исходной точке, `G` для COLL ground query, `Home` для сброса и Esc. Это free-fly camera без player physics, dynamic entities и gameplay streaming. `tools/test-world.ps1` проверяет streamed resources, sector candidates против linear scan, LOD-сцену, capacity block, async byte budget, clock/timed-object pools, BMP/Reset, переход из меню и управление на `net40` AnyCPU/x86 и `net9.0-windows`.

Для отдельной отладки DFF сохранён model preview:

```powershell
src\SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe --model player --game iii --root "D:\Games\GTA III"
```

В model preview работают orbit drag, mouse wheel zoom, стрелки, PgUp/PgDn, Home и Esc. Рендерятся оригинальные frame hierarchy, indices/normals/colors/materials и TXD, с directional light и Z-buffer. `tools/test-model.ps1` проверяет этот кадр на тех же конфигурациях, включая Reset и BMP pixels.

Прямой запуск без Launcher:

```powershell
src\SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe --menu --game iii --root "D:\Games\GTA III"
# Стартовые настройки и экспорт общего профиля:
src\SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe --menu --game iii --root "D:\Games\GTA III" --language 1 --resolution 800x600 --export-settings "%LOCALAPPDATA%\SharpGTA\launcher.ini"
# В следующий раз можно загрузить сохранённый state:
src\SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe --menu --import-settings "%LOCALAPPDATA%\SharpGTA\launcher.ini"
```

`tools/test-launcher.ps1` проверяет настоящий MSHTML DOM/bridge, темы, preview, запуск D3D9 и сохранение профиля. `tools/test-d3d9.ps1` отдельно проверяет triangle, textures-related ABI, reset/readback/present и x86/x64 layouts; `tools/test-model.ps1` и `tools/test-world.ps1` — оригинальные DFF/TXD и static world соответственно.

## D3D9 proof-of-concept

`SharpGTA.D3D9.exe` также сохраняет прежний triangle smoke без игровых ресурсов:

```powershell
src\SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe --hidden --frames 2 --exercise-reset --screenshot artifacts\d3d9-reset.bmp
```

D3D9 textures и static 3D vertex/index buffers используют managed pool; meshes рисуются через `DrawIndexedPrimitive` с оригинальными shared vertices/indices. Screen-space triangle/UI используют `DrawPrimitiveUP`, directional light и D24S8 работают для 3D. World models/TXD проходят через Core streaming slots с refcount/LRU; eviction освобождает GPU buffers/textures и CPU caches. Bytes доставляются двумя background channel'ами `AsyncByteCache` с byte-бюджетом, но decode/upload остаются на simulation thread, а gameplay streaming, IDE/IPL/COL channels и полный `CStreaming` lifecycle отсутствуют. Menu/glyph rendering, frontend state/layout и hitboxes разделены; Headless по-прежнему не создаёт GPU/окно. Это не полноценный GTA renderer и не Windows XP VM test.

## Проекты

- `src/SharpGTA.Core` — math, bounded I/O, форматы, модели, статический мир, deterministic runtime. Не знает о Win32/D3D и не ссылается на игровые профили.
- `src/SharpGTA.Games.III` — пути, лимиты и схемы GTA III.
- `src/SharpGTA.Games.VC` — отдельные пути, лимиты и схемы Vice City.
- `src/SharpGTA.Headless` — консольный host с null audio/presentation, без зависимости от D3D9.
- `src/SharpGTA.Presentation` — общий serializer launcher/frontend settings, без native UI и зависимостей от игровых профилей.
- `src/SharpGTA.D3D9` — Win32/D3D9 triangle/3D meshes, textures/glyphs, интерактивный III frontend, model и static world preview, readback/BMP и Reset.
- `src/SharpGTA.Launcher` — standalone MSHTML launcher с общими frontend settings и прямым запуском D3D9.
- `tests/SharpGTA.Tests` — собственный dependency-free test runner; синтетические данные не требуют игровых ресурсов.

`--query-zone` читает navigation/info/map zones и cull flags, `--query-path GROUP NODE` выводит world position и next link, `--query-path-nearest X Y Z [--path-kind car|ped|water] [--path-radius R]` ищет ближайший узел графа той же метрикой, что `CPathFind::FindNodeClosestToCoors`, а `--path-merge-distance R` пересобирает граф с другим радиусом схлопывания extern-пар (default 1 — как в оригинале). В D3D9 preview `--world --path-overlay` рисует рёбра графа линиями поверх мира, а `--path-route X Y Z X2 Y2 Z2` — найденный маршрут поверх них; это единственный способ увидеть сеть и маршрут глазами, а не по счётчикам. III paths читаются преимущественно из IDE, VC — из IPL; attached coords `/16` получают transform объекта, detached остаются мировыми. `PathGraph` делает тайл на каждый bound instance (как `m_mapObjects`), узлами считает только intern-слоты (`type 2`, как `m_pathNodes`), связывает их симметрично по `next` внутри тайла, схлопывает extern-пары ближе 1 unit в межтайловые связи, помечает компоненты связности как `CountFloodFillGroups`, хранит дистанции связей и ped-флаг `bCrossesRoad`, а `TryFindRoute`/`--query-path-route` считает кратчайший маршрут по этим дистанциям, car-связи несут `CCarPathLink` (направление, midpoint, полосы с наследованием и fallback), `--verify-paths` аудирует граф (симметрия, длины, компоненты, nearest round-trip, exit 4 при расхождении), а `--spawn-cars N` сеет машины, которые едут по сети с перепланированием маршрута (`PathFollowerModule`), `--spawn-peds N` — пешеходов по ped-сети тем же модулем; `VehicleGroundProbe` проверяет, что машины не висят над дорогой. `CPathFind::FindPath`, traffic lights, популация и AI ещё отсутствуют.

Подробности границ и ограничений — в [ARCHITECTURE.md](docs/ARCHITECTURE.md), результаты чтения двух реальных деревьев — в [REAL-ASSETS.md](docs/REAL-ASSETS.md), источники — в [REFERENCE-SOURCES.md](docs/REFERENCE-SOURCES.md), CI — в [CI.md](docs/CI.md), будущие multiplayer и межигровые ресурсы — в [IDEAS.md](IDEAS.md) и [RESOURCE-COMPATIBILITY.md](docs/RESOURCE-COMPATIBILITY.md).

## Брендинг

Видимое название, версия, tagline, URL и assembly metadata задаются **в одном месте** — `Directory.Build.props`:

```xml
<BrandName>SharpGTA</BrandName>
<BrandVersion>0.1.0</BrandVersion>
<BrandTagline>A managed engine for GTA III and Vice City</BrandTagline>
<BrandHomeUrl></BrandHomeUrl>
```

При сборке Core генерирует `Branding.g.cs`, поэтому консоль, Win32-окно D3D9 PoC и свойства файлов используют одни значения. Технические имена проектов/namespace/EXE не участвуют в пользовательском брендинге и не входят в state hash. Для смены публичного названия не нужно искать строки по всему коду. Юридические notices и имена оригинальных игр брендингом не заменяются.

## Git и локальные материалы

- `refs/` целиком исключён в `.gitignore`: туда можно складывать референсы, оригинальные ресурсы и приватные файлы.
- `bin/`, `obj/`, `artifacts/`, логи, saves и IDE-файлы также игнорируются.
- Git-репозиторий уже инициализирован. Локальное имя автора: `SharpGTA Agent`; глобальная конфигурация Git не менялась.

См. [NOTICE.md](NOTICE.md) по происхождению референсов и кредитам.
