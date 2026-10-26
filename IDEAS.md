# Идеи и будущие направления

## Multiplayer — обязательное будущее направление

Добавить multiplayer после того, как simulation, entity identity, streaming и script state станут управляемыми и воспроизводимыми. Учесть сетевой/headless/dedicated/master/relay опыт `../DPTDOOM2` и материалы `../reSA`, но не переносить Doom lockstep буквально.

### Основная модель: server-authoritative co-op/multiplayer

- **Dedicated headless server** владеет mission/script state, AI, physics rules, spawn/despawn, игровыми RNG и authority; он не занимает player slot и не требует GPU/окна.
- Клиент отправляет sequence-numbered input. Сервер рассылает versioned snapshots/deltas с baseline tick; late join получает полный snapshot и актуальный streaming/mission state.
- Локальный pawn/vehicle: prediction + reconciliation; удалённые сущности: interpolation. Рендер, particles, UI и presentation RNG не влияют на simulation RNG.
- Стабильные entity ID, явный ownership/authority transfer для транспорта, водителя/пассажиров, оружия и scripted objects. Не передавать managed pointers или raw C++ struct images.
- Interest management по streaming cell/zone/interior: не рассылать весь открытый мир каждому клиенту. VC interior area и III island/level collision учитываются профилями.
- Handshake: protocol version, simulation/gameplay version, III/VC profile, rules и сильный content/load-order manifest. Публичный бренд в compatibility fingerprint не входит.
- Надёжный канал для scripts/inventory/mission events; частые transforms можно отделить от reliable traffic. TCP/UDP выбрать после замеров и требований, а не заранее «TCP для всего».
- Reconnect/resume tokens через криптографический RNG; пароли и tokens не логировать. Rate limits, bounded messages, exact-read, total deadlines и причины каждого disconnect с самого начала.
- Directory/master — только discovery с lease/heartbeat; relay — только byte proxy. Ни один не владеет игровым миром. Direct connection предпочтителен, relay — fallback; NAT traversal отдельный этап.
- Инструменты из DPTDOOM2, которые стоит перенять идейно: `State@N`, replay input/snapshots, latency/jitter/fault-injection proxy, soak tests, master leases, диагностика фактического tick rate и stall/reconnect counters.

### Не делать

- Не строить общий multiplayer на чистом deterministic lockstep: float physics, AI, SCM, streaming order и iteration порядок открытого мира делают его хрупким. Small-coop lockstep допустим только как отдельный поздний эксперимент после доказанного детерминизма.
- Не отправлять весь world state каждый кадр и не делать relay/master «античитом». Античит основан на server authority и validation команд.
- Не смешивать dedicated с обычным конечным headless-тестом: сервер ждёт игроков бесконечно, тест при stall должен завершаться с диагностикой.

### Этапы multiplayer

1. Canonical snapshots, stable IDs, replay и divergence dump по tick/entity/field.
2. Loopback server + two headless clients, input/snapshot protocol и content handshake.
3. Snapshot/delta + late join + reconnect; authority для player/vehicle.
4. Prediction/interpolation, interest management и streamed mission state.
5. Dedicated console, master/relay, NAT, quotas и public-security review.
6. Long soak с latency/jitter/loss/reconnect и сравнением server/client state.

## Движок и инструменты

- Offline asset validator: единый отчёт по всем DAT/IDE/IPL/COL/IMG/GXT/DFF/TXD, golden manifests для конкретных версий ресурсов.
- Headless scenario format: scripted input, teleport/probe, `State@N`, ground/LOS assertions и первый divergence без скриншотов.
- CPU render capture отдельно от null headless; GPU screenshots сравнивать с tolerance, не как simulation hash.
- Встроенный asset cookbook/mod schema без изменения исходных ресурсов; explicit load order и content manifest пригодятся multiplayer.
- Отдельные compatibility profiles для PC revisions и re3-опций (например `USE_PS2_RAND`), не смешивать их с III/VC identity.
- После стабильных III/VC рассмотреть GTA SA. Не начинать SA сейчас: полнота и стабильность доступных reverse-engineered референсов сначала должны быть пересмотрены.
- Windows XP VM matrix: x86 .NET 4.0, минимальный DirectX 9, lost device/reset, low memory и long headless soak.
