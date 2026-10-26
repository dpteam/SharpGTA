# Long Term: reverse-engineering оригинальных EXE

## Когда это нужно

Сначала используются re3/reVC и полный `reSA/refs/librw`. Оригинальный EXE анализируется только для конкретного unresolved gap: неизвестного layout, порядка side effects, версии, алгоритма или различия retail/re3. Не реверсить бинарник вместо чтения уже доступного исходника.

Локальные цели, явно разрешённые пользователем:

| Target | SHA-256 |
|---|---|
| `refs/Grand Theft Auto 3/gta3.exe` | `9B1DDAFADEE27DCA2970159BE497C9A851061711EC79EC319340A1A35C0DAA28` |
| `refs/Grand Theft Auto Vice City/gta-vc.exe` | `8C2318D61BFFAAFF5E638449543DBF99EA932DCA85F297C7190D2EFD525B848F` |
| `refs/Grand Theft Auto Vice City/testapp.exe` | `D6ED749F6E5E05D637CAF60503DA5ED6CDB5A5EE1BB1F00426CC2FEDB0DE1A8B` |

III `testapp.exe` имеет тот же SHA-256, что `gta3.exe`, и не является независимым target. Версии не назначать по имени/размеру без version evidence. `gta3.exe`/`gta-vc.exe` — PC retail executable targets, не source files и не test assets для выполнения.

## Рабочий процесс

1. Сформулировать вопрос и expected evidence. Например: размер native mesh record 43/44 байта, поведение exact RW `0x33000`, opcode/save layout.
2. Зафиксировать SHA-256 и найти соответствующий source reference. Сохранить наблюдения/гипотезы/unknowns отдельно.
3. Использовать REA для static native analysis с активным binary target. Не запускать игровые EXE ради обычных тестов и не менять файлы в `refs/`.
4. Сохранять адреса/offsets, calling convention, struct fields, control flow и ограничения доказательства в отдельном отчёте. Не выдавать pseudocode за наблюдаемое runtime-поведение.
5. Подтвердить вывод независимым способом: retail asset sample, второй source/бинарь либо осторожный opt-in runtime probe. Synthetic test сам по себе не доказывает оригинальную семантику.
6. Перенести только необходимый алгоритм в managed код, добавить regression tests и ссылку на evidence. Не копировать ресурсы/бинарники в Git.

## Текущие кандидаты на будущее

- D3D8 pre-instanced/native geometry: противоречие 43/44-byte mesh record в librw. Пока локальные DFF non-native, поэтому это не блокер.
- Точная граница Clump version `0x33000`: re3/reVC используют `>=`, librw `>`.
- Поведение retail-специфичных SCM/save/profile cases, если реализации re3/reVC разойдутся или их не хватит.

Сейчас EXE-реверс ещё не требовался: полный librw найден, static DFF и D3D8 TXD имеют исходные layouts. Этот документ не утверждает, что анализ EXE уже выполнен.
