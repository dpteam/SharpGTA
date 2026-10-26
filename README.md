# SharpGTA

[![CI](https://github.com/dpteam/SharpGTA/actions/workflows/ci.yml/badge.svg)](https://github.com/dpteam/SharpGTA/actions/workflows/ci.yml)

Управляемый движок на C# для данных и игровой логики **GTA III** и **GTA Vice City**. Целевая поставка — **.NET Framework 4.0 / Windows XP SP3**, без сторонних runtime-библиотек. Исходные игровые ресурсы не входят в репозиторий и должны принадлежать пользователю.

> **Статус:** это рабочий headless-фундамент, а не playable-порт. Уже работают профили III/VC, DAT/IDE/IPL/COLL/IMG/GXT, static non-native DFF с geometry/materials/BinMesh, D3D8 TXD с mipmaps и RGBA decode, HAnim/Skin/MatFX readers, статические IPL-объекты и COLL-запросы. Animation runtime, SCM, динамической физики, пешеходов, транспорта, игрового renderer и звука пока нет; уже есть MSHTML Launcher и интерактивное меню GTA III на оригинальных TXD/GXT. См. [дорожную карту](docs/ROADMAP.md).

## Сборка

Нужен .NET SDK 9.0.318 или совместимый SDK из `global.json`. Для компиляции `net40` используется `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 — **только build-time targeting pack**, аналогично DPTDOOM2. Его DLL не попадают в поставку; сторонних runtime-библиотек и внешних native DLL нет. Restore использует стандартный источник NuGet, если targeting pack ещё не находится в локальном кэше.

```powershell
dotnet build SharpGTA.sln -c Release
powershell -NoProfile -File tools/test.ps1
# На Windows с рабочим D3D9: managed x86/x64 ABI + живой GPU smoke.
powershell -NoProfile -File tools/test-d3d9.ps1
# Если оригинальные деревья лежат в refs/, дополнительно:
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
SharpGTA.Headless.exe --root "D:\Games\GTA III" --standard --query-zone 812 -936 36 --query-path 0 0
SharpGTA.Headless.exe --root "D:\Games\GTA III" --gxt TEXT/AMERICAN.GXT --gxt-key MAIN
SharpGTA.Headless.exe --root "D:\Games\GTA III" --standard --rw-entry PLAYER.DFF
SharpGTA.Headless.exe --root "D:\Games\GTA III" --dff-entry PLAYER.DFF
SharpGTA.Headless.exe --root "D:\Games\GTA III" --dff-scan
SharpGTA.Headless.exe --root "D:\Games\GTA III" --txd-entry PLAYER.TXD
SharpGTA.Headless.exe --root "D:\Games\GTA III" --txd-scan
SharpGTA.Headless.exe --root "D:\Games\GTA III" --img MODELS/GTA3.IMG --dff-entry PLAYER.DFF --render-preview
SharpGTA.Headless.exe --root "D:\Games\GTA III" --dat DATA/CUSTOM.DAT --strict
```

`--rw`/`--rw-entry` показывают только проверенную по границам **инвентаризацию RenderWare chunks**. `--dff`/`--dff-entry` декодируют static non-native geometry/materials/BinMesh и HAnim/Skin/MatFX, а `--dff-scan` проверяет все DFF entries и отдельно считает empty/malformed и plugin counts. `--txd`/`--txd-entry` читают D3D8 dictionaries и декодируют все mip levels в RGBA; `--txd-scan` проверяет каждую TXD entry и все пиксели, отдельно считая unsupported PS2 rasters. `--render-preview` дополнительно проверяет managed mesh/material/texture pipeline через NullRenderer, по-прежнему без GPU/окна. `--state-at N` печатает диагностический FNV-1a-хеш состояния; это не криптографический хеш и не сетевой античит. Полный список опций: `--help`.

## Launcher и меню GTA III

Самый простой запуск:

```powershell
src\SharpGTA.Launcher\bin\Release\net40\SharpGTA.Launcher.exe
```

Launcher использует системный **MSHTML** (`WebBrowser`), embedded HTML/CSS/ES5 и `sharpgta://` bridge — без WebView2, CDN, HTTP server и внешних DLL. В нём можно выбрать папку игры, язык, разрешение, VSync/frame limiter и тему. Кнопка запуска открывает D3D9 frontend; состояние сохраняется в `%LOCALAPPDATA%\SharpGTA\launcher.ini` либо в `<EXE>/user/`, если рядом есть `portable.txt`.

Внутри меню работают мышь, стрелки, Enter, Esc и Left/Right для настроек. Используются оригинальные `MENU.TXD`, `FONTS.TXD` и GXT. Доступны Start Game/Options/Exit, подтверждения, слайдеры, переключатели, пять языков и смена разрешения. `New Game → Yes` открывает **интерактивный 3D preview PLAYER.DFF**, а не gameplay; save/load по-прежнему сообщает о неподключённых профилях. Vice City выбирается в Launcher как профиль данных, но её интерактивное меню ещё не реализовано и не подменяется III.

Прямой 3D preview:

```powershell
src\SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe --model player --game iii --root "D:\Games\GTA III"
```

В preview работают orbit drag, mouse wheel zoom, стрелки, PgUp/PgDn, Home и Esc. Рендерятся оригинальные frame hierarchy, indices/normals/colors/materials и TXD, с directional light и Z-buffer. `tools/test-model.ps1` проверяет этот кадр на `net40` AnyCPU/x86 и `net9.0-windows`, включая Reset и BMP pixels.

Прямой запуск без Launcher:

```powershell
src\SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe --menu --game iii --root "D:\Games\GTA III"
# Стартовые настройки и экспорт общего профиля:
src\SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe --menu --game iii --root "D:\Games\GTA III" --language 1 --resolution 800x600 --export-settings "%LOCALAPPDATA%\SharpGTA\launcher.ini"
# В следующий раз можно загрузить сохранённый state:
src\SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe --menu --import-settings "%LOCALAPPDATA%\SharpGTA\launcher.ini"
```

`tools/test-launcher.ps1` проверяет настоящий MSHTML DOM/bridge, темы, preview, запуск D3D9 и сохранение профиля. `tools/test-d3d9.ps1` отдельно проверяет triangle, textures-related ABI, reset/readback/present и x86/x64 layouts.

## D3D9 proof-of-concept

`SharpGTA.D3D9.exe` также сохраняет прежний triangle smoke без игровых ресурсов:

```powershell
src\SharpGTA.D3D9\bin\Release\net40\SharpGTA.D3D9.exe --hidden --frames 2 --exercise-reset --screenshot artifacts\d3d9-reset.bmp
```

D3D9 textures используют managed pool, A8R8G8B8, alpha-blended quads и fixed-function 3D meshes с directional light/D24S8. Menu/glyph rendering, frontend state/layout и hitboxes разделены; Headless по-прежнему не создаёт GPU/окно. Это не полноценный GTA renderer и не Windows XP VM test.

## Проекты

- `src/SharpGTA.Core` — math, bounded I/O, форматы, модели, статический мир, deterministic runtime. Не знает о Win32/D3D и не ссылается на игровые профили.
- `src/SharpGTA.Games.III` — пути, лимиты и схемы GTA III.
- `src/SharpGTA.Games.VC` — отдельные пути, лимиты и схемы Vice City.
- `src/SharpGTA.Headless` — консольный host с null audio/presentation, без зависимости от D3D9.
- `src/SharpGTA.Presentation` — общий serializer launcher/frontend settings, без native UI и зависимостей от игровых профилей.
- `src/SharpGTA.D3D9` — Win32/D3D9 triangle/3D meshes, textures/glyphs, интерактивный III frontend и model preview, readback/BMP и Reset.
- `src/SharpGTA.Launcher` — standalone MSHTML launcher с общими frontend settings и прямым запуском D3D9.
- `tests/SharpGTA.Tests` — собственный dependency-free test runner; синтетические данные не требуют игровых ресурсов.

`--query-zone` читает navigation/info/map zones и cull flags, `--query-path GROUP NODE` выводит world position и next link. III paths читаются преимущественно из IDE, VC — из IPL; attached coords `/16` получают transform объекта, detached остаются мировыми. Это ещё не AI pathfinding.

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
