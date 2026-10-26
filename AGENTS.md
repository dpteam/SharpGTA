# SharpGTA: правила работы

## Требования

- Основное ТЗ — `PROMPT.md`; актуальный статус — `docs/ROADMAP.md`. Общаться по-русски.
- Цель: полноценный движок GTA III/VC с нуля на C#, **net40**, Windows XP SP3, без сторонних runtime-библиотек. Win32/D3D9 через P/Invoke и COM допустимы; D3DX и внешние native DLL — нет.
- `net40` — поставка. `net9.0` / `net9.0-windows` — только удобные вторые runtime targets для тех же исходников и тестов. Код остаётся **C# 4**: без `Span`, `MathF`, records, tuples, `HashCode`, default interface methods и новых BCL API.
- Headless — обязательный основной сценарий разработки. Simulation/content core не должен создавать окно, GPU, звук или менять current directory. Null backends не заменяют gameplay streaming.
- Публичный бренд меняется только в `Directory.Build.props`; не разносить `SharpGTA` по UI, assembly metadata или compatibility fingerprints. Внутренние namespace и имена проектов — технические идентификаторы.
- Git не пушить и не делать коммиты/другие mutations без отдельной явной просьбы. Локальное имя уже настроено: `SharpGTA Agent`.

## Данные и референсы

- `refs/` полностью локальный и ignored; оригинальные игровые ресурсы не коммитить и не изменять. Доступны `refs/Grand Theft Auto 3` и `refs/Grand Theft Auto Vice City`; обязательно использовать их в real-asset smoke-тестах. Не выходить за пределы проекта без необходимости и явного контекста пользователя.
- re3/reVC — источники алгоритмов и форматов, не зависимости сборки. Сохранять кредиты и ограничения из `NOTICE.md`/`docs/REFERENCE-SOURCES.md`; не объявлять собственную лицензию без решения владельца.
- DPTDOOM2 — архитектурный образец, не источник для бездумного копирования кода/lockstep. Его GPL-код не переносить без отдельной лицензионной проверки.
- Локальные референсы `re3` и `reVC`: `../reSA/refs/re3` (master, `3233ffe1c4b99e8efb4c41c6794b4fce880cf503`) и `../reSA/refs/reVC` (miami, `a16fcd8d6a79e433c1c6e73d540f1bbe27e14164`). Полный `librw` найден отдельно в `../reSA/refs/librw` (`18532c2e13efbc1aa43f0b00b4459831f9414de0`); vendor-папки внутри re3/reVC пусты.
- Long Term: при unresolved gaps сначала исчерпать re3/reVC/librw, затем статически реверсить `refs/Grand Theft Auto 3/gta3.exe` и `refs/Grand Theft Auto Vice City/gta-vc.exe` по инструкции `docs/REVERSE-ENGINEERING.md`. Не запускать EXE для обычных asset-тестов и не заменять доступные исходники догадками по бинарнику.

## Архитектурные инварианты

- Core не ссылается на III/VC projects. `GameDefinition`, `DataSchema`, limits и game modules описывают различия; не собирать две версии через глобальный `#if VC`.
- **Raw numeric ID существует только внутри выбранного game context.** Не объединять weapon/model/level/opcode enums III и VC по имени или числу. Save/SCM profiles отдельные.
- Порядок DAT directives, simulation modules и побочных эффектов — часть совместимости. Не сортировать их ради удобства.
- Оригинальный `CTimer` variable-step: `timeStep = deltaMs / 1000 * 50`, clamp обычно `0.01..3`. `SimulationClock` — сознательный deterministic 30 Hz driver для headless, не утверждение об оригинальном fixed timestep.
- `GtaRandom` воспроизводит 32-bit PC CodeWarrior LCG, не `System.Random`. Альтернативный re3 `USE_PS2_RAND` — отдельный compatibility profile, если понадобится.
- `CMatrix` rows: Right/Forward/Up/Position. RW `right/up/at` ↔ GTA `right/forward/up` только на render boundary. `System.Numerics` не использовать.
- IMG — streaming с диска по секторам, не `ReadAllBytes` всего образа. Асинхронность позже допустима для I/O, но создание объектов и mutation мира — на simulation thread.
- Бинарные структуры читать field-by-field little-endian с bounds checks, не через `Marshal`/C++ memory image. Текстовые числа — только `InvariantCulture`; asset names — ASCII case-insensitive, GXT keys — ordinal case-sensitive.
- `StaticWorld` сейчас только IPL + COLL queries, не полный `CWorld`. Parsed IPL scale сохраняется, но не применяется — как в исходном loader. COLL lines сохраняются, но raycast по ним не ведётся. VC COLL из IMG грузится целиком до первого IPL; bounds-based streaming/eviction ещё не реализованы.
- RenderWare inventory не является DFF/TXD decoder. Layouts брать из `reSA/refs/librw`, GTA integration — из re3/reVC; не выдумывать native formats. Static non-native DFF реализован (`DffReader`, `--dff[-entry]`, полный `--dff-scan`). PC D3D8 TXD/mipmaps и RGBA decode реализованы (`TxdReader`, `TxdPixelDecoder`, `--txd[-entry]`, `--txd-scan`). Skin/HAnim/MatFX binary readers реализованы (`DffPluginReader`, old/split Skin, texture effects); animation/skinning/render behavior ещё нет. NATIVE geometry, PAL4/DXT2/DXT4 и PS2-specific decoders остаются отдельными этапами.
- `ZoneWorld`/`PathWorld` — typed data и queries, не полный `CTheZones` simulation или `CPathFind`. Path coords `/16`; attached groups требуют IPL transform, unresolved не выдумывать. VC detached/water paths остаются в исходном порядке.
- `Rendering/` содержит только managed mesh/material/texture DTO и `NullRenderDevice`; никаких Win32/D3D imports в Core. `--render-preview` проверяет resources без GPU, но не является изображением или полным renderer.
- `src/SharpGTA.D3D9` — отдельный Win32/D3D9 proof-of-concept без зависимости Headless от него: direct vtable `StdCall`, fixed-function screen-space triangle, managed A8R8G8B8 textures/quads, lockable back-buffer readback/BMP до `Present` и explicit `Reset`. Managed ABI/layout/BMP tests не создают окно/GPU; живой smoke и полноценный игровой renderer — разные уровни проверки. Vertex buffers/shaders/state cache и полное device-loss recovery ещё впереди.
- `src/SharpGTA.Launcher` — standalone WinForms/MSHTML launcher (не WebView2), embedded ES5 HTML/CSS и bridge через отменяемую `sharpgta://` навигацию. Не использовать `window.external` как обязательный механизм; никаких CDN/HTTP server/внешних DLL. Launcher запускает D3D9 напрямую и хранит общие frontend settings; оригинальные деревья не изменяет. Для тестов есть изолированные `SHARPGTA_LAUNCHER_*` variables, не включать их в обычный запуск.
- GTA III interactive frontend использует `src/SharpGTA.Core/Frontend` для state/layout/GXT/font metrics и `SharpGTA.D3D9` для textures/glyphs/input. `SharpGTA.Presentation/FrontendPreferencesState` общий для launcher/menu; не делать расходящиеся дубликаты сериализации. VC frontend пока не реализован, не подменять его III assets. Gameplay bootstrap/save profiles пока не подключены и должны сообщаться честно.
- Отсутствующие оригинальные ресурсы не должны ломать synthetic tests. Для real-asset checks честно сообщать `SKIP/NOT RUN`, не выдавать за успех.

## Проверки

```powershell
dotnet build SharpGTA.sln -c Release
powershell -NoProfile -File tools/test.ps1
powershell -NoProfile -File tools/check-standalone.ps1
powershell -NoProfile -File tools/check-xp.ps1
powershell -NoProfile -File tools/test-d3d9.ps1
powershell -NoProfile -File tools/test-launcher.ps1
# При наличии оригинальных деревьев в refs/:
powershell -NoProfile -File tools/test-real-assets.ps1
```

`tools/test.ps1` запускает тесты на `net9.0-windows` и `net40`, плюс повторяемость headless. `tools/test-d3d9.ps1` — отдельный Windows/GPU-сценарий для managed x86/x64 ABI и живого D3D9 create/reset/draw/readback/present на `net40` AnyCPU/x86 и `net9.0-windows`, включая BMP pixel assertions; он не является обязательным headless CI-шагом. `tools/test-real-assets.ps1` проверяет оба дерева в `refs/`, если они есть; отсутствие честно печатает `SKIP`. После изменений обязательны обе runtime-проверки на Windows. Живой Windows XP SP3 / .NET 4.0 пока не проверялся; современная Windows/.NET 4.8 и статические проверки не заменяют XP VM smoke test. Compile-only `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 разрешён, но его DLL и любые другие пакеты в поставку не попадают. CI: `.github/workflows/ci.yml`, описание `docs/CI.md`; внешние игровые ресурсы в CI не скачивать.
- Межигровое смешивание ресурсов пока только Long Term-дизайн `docs/RESOURCE-COMPATIBILITY.md`: отдельные target profiles, semantic manifests, adapters и staging/rollback. Не объединять raw ID/SCM/saves и не изменять оригинальные деревья для conversion.

При добавлении Win32: Unicode entry points с `W` + `ExactSpelling`, `SetLastError` только при чтении ошибки, срок жизни delegates/pins явный. При добавлении D3D9: точные vtable/HRESULT/layout tests x86/x64, освобождение COM и device lost/reset, без обязательного device в headless. Обновить XP import allowlist и этот файл.
