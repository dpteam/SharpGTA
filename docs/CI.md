# GitHub Actions CI

Workflow: `.github/workflows/ci.yml`.

Запускается на `push` и `pull_request` в `main`, а также вручную через **Run workflow** (`workflow_dispatch`). Работает на `windows-latest`, поскольку `net40` и `.NET Framework` проверяются на Windows.

## Шаги

1. Устанавливается .NET SDK `9.0.318` из `global.json`.
2. Кэшируется только `Microsoft.NETFramework.ReferenceAssemblies` 1.0.3.
3. `dotnet restore SharpGTA.sln`.
4. `dotnet build SharpGTA.sln -c Release --no-restore`.
5. `tools/test.ps1 -NoBuild`: все synthetic tests на `net9.0` и `net40`, повторяемость headless.
6. `tools/test-real-assets.ps1`: проверки оригинальных деревьев, если они есть.
7. `tools/check-standalone.ps1`.
8. `tools/check-xp.ps1`.
9. Создаётся `artifacts/package`, `tools/package-artifact.ps1` проверяет состав и запуск EXE, затем ZIP.
10. `actions/upload-artifact` публикует готовый net40-билд и checkout/docs.

Все проверки обязательны; ошибка любого шага ломает job. CI использует только `contents: read` и не требует secrets.

## Скачиваемые артефакты

После успешного workflow: **Actions → выбранный run → Artifacts**.

- **`SharpGTA-net40-Windows`** — ZIP и распакованная папка: `SharpGTA.Headless.exe`, managed DLL, configs/PDB, `README.md`, `NOTICE.md`, `docs/REAL-ASSETS.md`, `docs/CI.md`, `docs/ROADMAP.md`. Target — .NET Framework 4.0; на современной Windows можно запускать сразу. Для оригинальных данных передать `--root` локальной игровой папке.
- **`SharpGTA-Source`** — исходники решения и документация, без `bin/obj` и без игровых ресурсов. Git checkout также доступен через **Code → Download ZIP**.

Срок хранения — 30 дней. `refs/`, GTA EXE/IMG/DFF/TXD/COL/GXT/SCM/save и сторонние runtime DLL в артефакты не входят. Build-time reference assemblies в пакет не копируются.

## Ассеты и ограничения

GitHub не скачивает GTA, IMG, EXE или другие игровые ресурсы. На чистом runner каталог `refs/` отсутствует, поэтому real-asset script печатает `SKIP` и завершается успешно. Это не считается real-asset pass. Полные локальные результаты обеих игр документируются отдельно в `docs/REAL-ASSETS.md`.

`net40` собирается и запускается с помощью .NET Framework на Windows runner. `net9.0` используется для второго runtime. **GitHub CI не является проверкой Windows XP**: XP static checks дополняются обязательным будущим XP VM smoke test. D3D9/runtime graphics пока не проверяются.

Те же команды доступны локально; см. `AGENTS.md` и `README.md`.
