# SharpGTA

[![CI](https://github.com/dpteam/SharpGTA/actions/workflows/ci.yml/badge.svg)](https://github.com/dpteam/SharpGTA/actions/workflows/ci.yml)

Управляемый движок на C# для данных и игровой логики **GTA III** и **GTA Vice City**. Целевая поставка — **.NET Framework 4.0 / Windows XP SP3**, без сторонних runtime-библиотек. Исходные игровые ресурсы не входят в репозиторий и должны принадлежать пользователю.

> **Статус:** это рабочий headless-фундамент, а не playable-порт. Уже работают профили III/VC, DAT/IDE/IPL/COLL/IMG/GXT, static non-native DFF с geometry/materials/BinMesh, D3D8 TXD с mipmaps и RGBA decode, статические IPL-объекты и COLL-запросы. Skin/HAnim/MatFX, SCM, динамической физики, пешеходов, транспорта, рендера и звука пока нет. См. [дорожную карту](docs/ROADMAP.md).

## Сборка

Нужен .NET SDK 9.0.318 или совместимый SDK из `global.json`. Для компиляции `net40` используется `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 — **только build-time targeting pack**, аналогично DPTDOOM2. Его DLL не попадают в поставку; сторонних runtime-библиотек и внешних native DLL нет. Restore использует стандартный источник NuGet, если targeting pack ещё не находится в локальном кэше.

```powershell
dotnet build SharpGTA.sln -c Release
powershell -NoProfile -File tools/test.ps1
# Если оригинальные деревья лежат в refs/, дополнительно:
powershell -NoProfile -File tools/test-real-assets.ps1
```

Поставляемая конфигурация — `net40`. `net9.0` существует только для быстрого запуска тех же исходников и тестов на современном runtime. Зелёный `net9.0` не считается доказательством совместимости с XP.

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
SharpGTA.Headless.exe --root "D:\Games\GTA III" --gxt TEXT/AMERICAN.GXT --gxt-key MAIN
SharpGTA.Headless.exe --root "D:\Games\GTA III" --standard --rw-entry PLAYER.DFF
SharpGTA.Headless.exe --root "D:\Games\GTA III" --dff-entry PLAYER.DFF
SharpGTA.Headless.exe --root "D:\Games\GTA III" --dff-scan
SharpGTA.Headless.exe --root "D:\Games\GTA III" --txd-entry PLAYER.TXD
SharpGTA.Headless.exe --root "D:\Games\GTA III" --txd-scan
SharpGTA.Headless.exe --root "D:\Games\GTA III" --dat DATA/CUSTOM.DAT --strict
```

`--rw`/`--rw-entry` показывают только проверенную по границам **инвентаризацию RenderWare chunks**. `--dff`/`--dff-entry` декодируют static non-native geometry/materials/BinMesh, а `--dff-scan` проверяет все DFF entries и отдельно считает empty/malformed. `--txd`/`--txd-entry` читают D3D8 dictionaries и декодируют все mip levels в RGBA; `--txd-scan` проверяет каждую TXD entry и все пиксели, отдельно считая unsupported PS2 rasters. Это ещё не GPU rendering. `--state-at N` печатает диагностический FNV-1a-хеш состояния; это не криптографический хеш и не сетевой античит. Полный список опций: `--help`.

## Проекты

- `src/SharpGTA.Core` — math, bounded I/O, форматы, модели, статический мир, deterministic runtime. Не знает о Win32/D3D и не ссылается на игровые профили.
- `src/SharpGTA.Games.III` — пути, лимиты и схемы GTA III.
- `src/SharpGTA.Games.VC` — отдельные пути, лимиты и схемы Vice City.
- `src/SharpGTA.Headless` — консольный host с null audio/presentation.
- `tests/SharpGTA.Tests` — собственный dependency-free test runner; синтетические данные не требуют игровых ресурсов.

Подробности границ и ограничений — в [ARCHITECTURE.md](docs/ARCHITECTURE.md), результаты чтения двух реальных деревьев — в [REAL-ASSETS.md](docs/REAL-ASSETS.md), источники — в [REFERENCE-SOURCES.md](docs/REFERENCE-SOURCES.md), CI — в [CI.md](docs/CI.md), будущие multiplayer и межигровые ресурсы — в [IDEAS.md](IDEAS.md) и [RESOURCE-COMPATIBILITY.md](docs/RESOURCE-COMPATIBILITY.md).

## Брендинг

Видимое название, версия, tagline, URL и assembly metadata задаются **в одном месте** — `Directory.Build.props`:

```xml
<BrandName>SharpGTA</BrandName>
<BrandVersion>0.1.0</BrandVersion>
<BrandTagline>A managed engine for GTA III and Vice City</BrandTagline>
<BrandHomeUrl></BrandHomeUrl>
```

При сборке Core генерирует `Branding.g.cs`, поэтому консоль, окна в будущем и свойства файлов используют одни значения. Технические имена проектов/namespace/EXE не участвуют в пользовательском брендинге и не входят в state hash. Для смены публичного названия не нужно искать строки по всему коду. Юридические notices и имена оригинальных игр брендингом не заменяются.

## Git и локальные материалы

- `refs/` целиком исключён в `.gitignore`: туда можно складывать референсы, оригинальные ресурсы и приватные файлы.
- `bin/`, `obj/`, `artifacts/`, логи, saves и IDE-файлы также игнорируются.
- Git-репозиторий уже инициализирован. Локальное имя автора: `SharpGTA Agent`; глобальная конфигурация Git не менялась.

См. [NOTICE.md](NOTICE.md) по происхождению референсов и кредитам.
