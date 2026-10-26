# Референсы и границы использования

## Снапшоты

| Назначение | Локальный путь от корня harness | Snapshot |
|---|---|---|
| GTA III algorithms/data behavior | `reSA/refs/re3` | `3233ffe1c4b99e8efb4c41c6794b4fce880cf503`, master |
| Vice City algorithms/data behavior | `reSA/refs/reVC` | `a16fcd8d6a79e433c1c6e73d540f1bbe27e14164`, miami |
| RenderWare DFF/TXD binary layouts | `reSA/refs/librw` | `18532c2e13efbc1aa43f0b00b4459831f9414de0` |
| Standalone/headless/testing/MP ideas | `DPTDOOM2` | локальный рабочий снапшот, SHA не фиксировался |

`SharpGTA/refs/` — новый локальный ignored-каталог для материалов пользователя. Он не совпадает с `reSA/refs/` и ничего не добавляет в сборку автоматически.

## Основные файлы для переноса

- Initialization/game loop: `src/core/Game.cpp`, `src/core/main.cpp`, `src/skel/win/win.cpp`, `src/core/Timer.cpp`, `src/core/Clock.cpp`.
- Files/streaming: `src/core/FileMgr.cpp`, `Directory.cpp`, `CdStream.cpp`, `Streaming.cpp`, `FileLoader.cpp`.
- Math: `src/math/Vector.*`, `Matrix.*`, `Quaternion.*`, `maths.h`.
- Models/world: `src/modelinfo/*`, `src/entities/Entity.*`, `Physical.*`, `src/core/World.*`, `Pools.*`, `templates.h`.
- Collision: `src/collision/ColModel.*`, `ColTriangle.*`, `Collision.*`, `CompressedVector.h`, VC `ColStore.*`.
- Text: `src/text/Text.*`; IMG/DIR layout дополнительно сверен с `Directory.*`.
- DFF/TXD integration: `src/rw/ClumpRead.cpp`, `TexRead.cpp`, `NodeName.cpp`, `src/fakerw/*`.
- RenderWare layouts в `reSA/refs/librw/src`: `base.cpp`, `frame.cpp`, `clump.cpp`, `geometry.cpp`, `geoplg.cpp`, `texture.cpp`, `hanim.cpp`, `skin.cpp`, `matfx.cpp`, `d3d/d3d8.cpp`, `d3d/d3d.cpp`, `image.cpp`.
- Script/animation следующих этапов: `src/control/Script*`, `src/animation/AnimManager.cpp`.

Абсолютные source paths ведут к соответствующему файлу в обоих снапшотах, если не указано VC-only/III-only.

## Важные ограничения референсов

1. re3 и reVC — разошедшиеся ветки, не один `core + #if VC`. Числовые enums/limits/save/SCM layouts нельзя объединять механически.
2. Внутри `re3/vendor/librw` и `reVC/vendor/librw` файлов нет, но полный отдельный snapshot найден в `reSA/refs/librw`. Он даёт проверяемые DFF/TXD layouts; managed decoder реализуется в проекте, native librw в сборку/поставку не включается.
3. Оригинальный game loop не fixed timestep. Наш 30 Hz clock — headless driver для повторяемости; compatibility wall-clock timer будет отдельным компонентом.
4. re3/reVC содержат исправления и optional behavior. Сначала фиксируется PC data/runtime profile; такие варианты, как `USE_PS2_RAND`, не должны незаметно подменять PC original.
5. DPTDOOM2 GPL и его `refs/` не дают автоматического права копировать код. В SharpGTA перенесены только архитектурные идеи: net40 shipping, headless/null services, dependency-free tests, state hashes, diagnostics и разделение master/relay/simulation.
6. Оригинальные игровые ресурсы для real-asset тестов в проект не копируются. Synthetic tests не доказывают совместимость со всеми retail variants.

## Кредиты и лицензии

См. корневой `NOTICE.md`. Не добавлять чужие LICENSE/copyright headers механически и не назначать свою лицензию на производные материалы без проверки владельцем.
