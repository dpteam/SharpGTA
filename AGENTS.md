# SharpGTA: правила работы

## Требования

- Основное ТЗ — `PROMPT.md`; актуальный статус — `docs/ROADMAP.md`. Общаться по-русски.
- Цель: полноценный движок GTA III/VC с нуля на C#, **net40**, Windows XP SP3, без сторонних runtime-библиотек. Win32/D3D9 через P/Invoke и COM допустимы; D3DX и внешние native DLL — нет.
- `net40` — поставка. `net9.0` — только удобный второй runtime для тех же исходников и тестов. Код остаётся **C# 4**: без `Span`, `MathF`, records, tuples, `HashCode`, default interface methods и новых BCL API.
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
- RenderWare inventory не является DFF/TXD decoder. Layouts брать из `reSA/refs/librw`, GTA integration — из re3/reVC; не выдумывать native formats. Static non-native DFF реализован (`DffReader`, `--dff[-entry]`, полный `--dff-scan`). NATIVE, Skin/HAnim/MatFX, TXD pixels и PS2 остаются отдельными этапами до явной реализации.
- Отсутствующие оригинальные ресурсы не должны ломать synthetic tests. Для real-asset checks честно сообщать `SKIP/NOT RUN`, не выдавать за успех.

## Проверки

```powershell
dotnet build SharpGTA.sln -c Release
powershell -NoProfile -File tools/test.ps1
powershell -NoProfile -File tools/check-standalone.ps1
powershell -NoProfile -File tools/check-xp.ps1
# При наличии оригинальных деревьев в refs/:
powershell -NoProfile -File tools/test-real-assets.ps1
```

`tools/test.ps1` запускает тесты на `net9.0` и `net40`, плюс повторяемость headless. `tools/test-real-assets.ps1` проверяет оба дерева в `refs/`, если они есть; отсутствие честно печатает `SKIP`. После изменений обязательны обе runtime-проверки на Windows. Живой Windows XP SP3 / .NET 4.0 и D3D9 пока не проверялись; статические проверки не заменяют VM smoke test. Compile-only `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3 разрешён, но его DLL и любые другие пакеты в поставку не попадают.

При добавлении Win32: Unicode entry points с `W` + `ExactSpelling`, `SetLastError` только при чтении ошибки, срок жизни delegates/pins явный. При добавлении D3D9: точные vtable/HRESULT/layout tests x86/x64, освобождение COM и device lost/reset, без обязательного device в headless. Обновить XP import allowlist и этот файл.
