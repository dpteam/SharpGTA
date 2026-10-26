# Long Term: межигровая загрузка ресурсов

Цель — позволить осмысленно комбинировать ресурсы GTA III и Vice City: отдельные модели и текстуры, наборы объектов, перенос карты или варианты «III в VC» / «VC в III». Это **дизайн будущей подсистемы**, а не заявление о текущей совместимости. Сейчас игры загружаются только в независимых профилях.

## Главное правило

**Нельзя слепо объединять две папки игры, IMG, IDE/IPL и глобальные raw ID.** У III и VC различаются model/weapon/level/opcode numbers, лимиты, IDE/IPL records, collision stores, scripts, animations и saves. Одинаковое имя или число не означает одинаковый ресурс.

Всегда есть явный **target game context**, определяющий правила симуляции. Source-пакет ресурсов не меняет эти правила незаметно.

## Три разных режима

1. **Shared asset packs — приоритетный первый этап.** Модели, текстуры и, после проверки, collision загружаются из source game в target context через manifest и compatibility adapter. Это не перенос всей игры.
2. **Offline map/data conversion.** Для переноса карты генерируются новые IDE/IPL, semantic bindings и target IDs в `artifacts/` или отдельном пользовательском пакете. Paths, zones, handling, scripts и missions конвертируются отдельными правилами, а не «угадываются» из похожих названий. Исходные игровые деревья не изменяются.
3. **Несколько изолированных game contexts.** Для сосуществования двух игр каждая сохраняет свой registry, SCM, assets, saves и simulation. Возможный переход между мирами идёт через явный gateway, а не общий namespace. Это безопаснее попытки сделать универсальный III+VC runtime.

## Manifest вместо неявной магии

Каждый пакет описывается versioned manifest:

- schema version, стабильный package ID и semantic version;
- source game/profile/revision и поддерживаемые target profiles;
- режим `shared-assets`, `converted-map` или `isolated-world`;
- список файлов, типов, размеров и SHA-256;
- стабильные semantic keys ресурсов, зависимости и required/optional статус;
- разрешённые duplicate/override rules и детерминированный load order;
- supported features/plugins, ограничения памяти и compatibility diagnostics;
- преобразования ID/имён только через явные таблицы с доказанной семантикой.

Manifest — данные, не код. Он не загружает DLL/ASI, не запускает EXE и не разрешает пути вне пакета. Multiplayer позже использует тот же manifest в content handshake.

## Совместимость по типам ресурсов

| Ресурс | Безопасный подход |
|---|---|
| DFF | Декодировать в neutral CPU DTO с source version/features. Static non-native geometry можно адаптировать; NATIVE, Skin/HAnim/MatFX требуют отдельных подтверждённых adapters, иначе отказ. |
| TXD | D3D8 levels и RGBA decode независимы от raw model IDs. Texture names имеют package namespace. PS2/unknown formats не подменять похожими PC данными. |
| COLL | Привязывать к semantic model и проверять bounds/transforms. III level collision и VC streamed slots конвертируются в target store, не смешиваются напрямую. |
| IDE/IPL | Raw IDs остаются только в source parser. Target registry выделяет новые IDs по semantic map; порядок records сохраняется. VC-only `area`, `weap`, paths и flags требуют явных правил. |
| GXT | Keys и mission tables namespace-ятся. Raw code units/encoding сохраняются; нельзя объединять дубликаты молча. |
| IFP/animations | Bone/node mapping и версии обязательны. Нет доказанного mapping — ресурс отклоняется. |
| SCM/scripts | **Не загружать из другой игры по умолчанию.** Opcode tables, script space, globals и mission assumptions различаются; нужна отдельная проверенная конверсия/адаптер. |
| Handling/configs | Переносить только field-by-field по целевой схеме, с указанием defaults и game-specific последствий. |
| Saves | III ↔ VC не читаются как общий формат. Нужны новые saves либо offline converter с явной версией и отчётом о потерях. |

## Отказоустойчивость

Загрузка идёт транзакцией:

1. Проверить manifest, пути, hashes, размеры, dependencies и target compatibility.
2. Прочитать всё в staging context с bounded I/O, лимитами памяти/counts и отменой streaming.
3. Проверить ссылки model ↔ collision ↔ textures ↔ animations, duplicate rules и обязательные resources.
4. Выполнить dry-run summary и ground/basic queries; обязательные ошибки блокируют активацию.
5. Атомарно активировать готовый набор. До этого момента live world не меняется.
6. При ошибке освободить staging и оставить прежний мир нетронутым. При hot reload иметь rollback к предыдущему набору.

`required` resource отсутствует или не поддержан — пакет не активируется. `optional` resource можно пропустить только с явным диагностическим placeholder и записью в отчёт. **Никаких тихих fallback на похожий объект, повреждённой partial-world или непредсказуемого last-wins.**

## Безопасность

Моды и ресурсы считаются непроверенным вводом:

- только root-scoped relative paths; запрет traversal, volume syntax и reparse-point escape;
- явные size/count/depth/memory limits, checked arithmetic, отказ от malformed offsets;
- hashes и размер файла проверяются до активации; load order детерминирован;
- никакого исполняемого кода, C++ pointers, raw memory images или неявного scripting;
- отчёт различает missing, unsupported, corrupt, conflict и budget exceeded;
- конфликт одинаковых semantic keys — ошибка, если manifest не содержит специально разрешённого override;
- сервер будущего multiplayer не принимает произвольные пакеты от клиента; content allowlist и совпадение manifest обязательны.

## Набор будущих проверок

- Один validated DFF/TXD/COLL pack III → VC и VC → III с semantic ID remap.
- Дубликаты имён, отсутствующая зависимость, неподдержанный plugin и превышение бюджета.
- Truncated/malicious offsets, path traversal, hash mismatch и неизвестная версия manifest.
- Отказ cross-game SCM/save по умолчанию и понятная диагностика.
- Rollback после ошибки: прежний `State@N`, registry и загруженные resources не меняются.
- Повторная загрузка даёт тот же manifest/hash и порядок объектов.
- Real-asset conversion выполняется только в staging/artifacts; оригинальные `refs/` остаются неизменными.
- Отдельные прогоны на net40/net9 и Windows XP, когда появится реализация.

## Практический порядок реализации

1. Semantic resource keys + versioned manifest + hash validator, без смены текущих game profiles.
2. Read-only analyzer отдельных DFF/TXD/COLL и compatibility matrix.
3. Staging context + transaction/rollback для shared asset packs.
4. Offline IDE/IPL converter и новые target IDs.
5. Отдельные animation/config adapters; SCM/save только после самостоятельных профилей и regression suites.
6. При необходимости несколько isolated game contexts и явный переход между ними.
