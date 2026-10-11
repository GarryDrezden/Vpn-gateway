[Русский](README.md) · [English](README.en.md)

# VPN Route

**VPN Route — выборочная маршрутизация приложений и сайтов через VPN в Windows.**

VPN Route позволяет направлять через VPN только выбранные приложения и сайты, оставляя остальной трафик Windows на обычном подключении. Приложение управляет VPN-соединением, маршрутизацией приложений, браузерными правилами и диагностикой системы.

---

Приложение для Windows 10/11 x64: по умолчанию интернет идёт **напрямую (Direct)**, возможно сосуществование с **корпоративным VPN**, а через **личный туннель OpenVPN** отправляются **только выбранные приложения** (и при необходимости отдельные назначения).

Типовой сценарий: **Cursor через личный VPN, Git — напрямую.**

## Версия продукта

**VPN Route 1.0.0 RC19** (числовая/файловая версия **1.0.0.19**). Публичный стабильный релиз **v1.0.0** ещё не выпущен. Расширение Chromium: штатный ID `lfaekfalhkgmbfdjjlfcalanhijeaien`, Native Host `com.vpnroute.browser`, протокол Browser Routing **v1**.

## Актуальная базовая линия тестов

Локально на текущей `main` (`dotnet test SelectiveVpnRouter.sln -c Release`):

- **925** тестов всего (**625** Core + **264** BrowserRouting + **36** Proxy)

## Вехи (проверено на Windows 10/11 x64)

### Веха 1 — `per-process-routing-pass-v1`

Контролируемый путь **Probe.exe**: прозрачная TCP-маршрутизация по процессам с одновременной изоляцией Direct-трафика.

Правило приложения → WFP `FWPM_CONDITION_ALE_APP_ID` → `FWPM_LAYER_ALE_CONNECT_REDIRECT_V4` → callout KMDF (`SelectiveVpnCallout.sys`) → прозрачный прокси на localhost → восстановление контекста перенаправления → исходящий сокет, привязанный к VPN.

### Веха 2 — `real-app-vpn-egress-pass-v1`

Проверено на реальном внешнем приложении: `C:\Windows\System32\curl.exe` с временным правилом APP_ID.

- HTTPS `200 OK` через `curl --resolve` (DNS обходится для контролируемого теста)
- Зафиксирован публичный IP VPN (`91.184.250.53`)
- Тот же исполняемый файл без правила возвращает публичный IP Direct (`83.143.157.1`)
- Поток прокси: `vpnOutboundConnected=True`, `status=Closed`, `egressVerified=True`

Полная цепочка: curl.exe → фильтр WFP → callout → прозрачный прокси → исходящий через VPN → TLS/HTTP → выход в VPN.

### Веха 3 — `browser-vpn-egress-pass-v1`

Проверено на реальном браузере: `C:\Program Files (x86)\Google\Chrome\Application\chrome.exe` с временным правилом APP_ID.

- Chrome открыл `https://api.ipify.org` и показал публичный IP VPN (`91.184.250.53`)
- Direct-трафик без правила остаётся Direct (`curl.exe -4 https://api.ipify.org` → `83.143.157.1`)
- Цель Test Center `api.ipify.org:443` → **TARGET CLOSED / ПРОЙДЕНО** (`WFP=True`, `Proxy=True`, `Context=True`, `Bound=True`, `Connected=True`, `Route=Vpn`)
- Сопоставление целевого потока изолирует разрешённые IP цели; фоновые потоки Chrome (Google/CDN) не перезаписывают успех цели
- Готовность VPN-адаптера: APIPA/Tentative отклоняются; управляемое обнаружение DAD; безопасный таймаут IPC ConnectVpn и откат

Полная цепочка: chrome.exe → ALE APP_ID / WFP → callout → прозрачный прокси → исходящий через VPN → HTTPS → выход в VPN.

### Веха 4 — `unicode-appid-routing-pass-v1`

Пути к исполняемым файлам Unicode и не-ASCII: **ПРОЙДЕНО** (нормализованный ALE_APP_ID после канонической строки Fwpm).

- Матрица нормализации: `counterExamples=0` (ASCII, кириллица, латиница с умлаутом, смешанные пути)
- Сквозной redirect Unicode Probe: результат ПРОЙДЕНО, публичный IP VPN (`91.184.250.53`)
- Telegram (`Telegram Desktop\Telegram.exe` в профиле пользователя с Unicode): штатный фильтр + сценарии UI `Route=VPN`

Техническая заметка: [docs/unicode-ale-app-id.md](docs/unicode-ale-app-id.md)

Офлайн-регрессия (после подключения VPN):

```powershell
.\scripts\test-runtime-diagnostic-preflight.ps1
.\scripts\run-wfp-appid-regression.ps1
```

### Веха 5 — `vpn-lifecycle-stability-pass-v1`

Очистка при подключении/отключении/переподключении, маршрутизация после второго переподключения, корректное состояние при отключении.

- `VpnRoutingReady` управляет UI и офлайн-раннерами (полный конвейер, а не одна строка лога).
- Парсер OpenVPN отклоняет `Initialization Sequence Completed With Errors`.
- Офлайн: `test-vpn-lifecycle-preflight.ps1`, `run-vpn-lifecycle-regression.ps1` (начальный + дымовой прогон маршрутизации после переподключения, финальная очистка).

Техническая заметка: [docs/vpn-lifecycle-stability.md](docs/vpn-lifecycle-stability.md)

### Веха 6 — `application-discovery-routing-pass-v1`

UX правил приложений, обнаружение MSIX, стабильная привязка packaged-приложений, цели вспомогательной маршрутизации, сохранение localhost OAuth, UX диагностики, укрепление разработческой публикации и жизненного цикла драйвера.

- **Обнаружение приложений V1:** внутренние вкладки (Маршрутизация / Установленные / Запущенные); меню «Пуск», App Paths, реестр Uninstall, packaged `AppxManifest`; поиск, иконки, пакетное добавление; helper-процессы скрыты из списка «Установленные».
- **Привязка packaged-приложений:** `PackagedBinding` в правилах (PFN + ApplicationId + SID пользователя); перепривязка при load/save/обновлении политики; связанные helper из манифеста делят VPN WFP-фильтры с одной логической строкой UI (например ChatGPT).
- **Loopback:** для каждой VPN-цели приложения — высокоприоритетный TCP PERMIT `127.0.0.0/8` на `ALE_CONNECT_REDIRECT_V4` до redirect callout; `FWP_ACTION_PERMIT = 0x00001002`; Test Center **OAuth localhost** — ПРОЙДЕНО.
- **Диагностика:** категории Test Center, закреплённая панель результата, перенос строк в журнале, UTF-8 вывод probe/service, диагностика **целей маршрутизации packaged-приложений**.
- **Разработка:** сборка драйвера в изолированную промежуточную папку; канонический рабочий драйвер в структуре публикации; скрипты развёртывания/отката (см. `scripts`).

Известное ограничение: OAuth **token exchange** ChatGPT/Codex может по-прежнему возвращать region 403 при выборочной маршрутизации, несмотря на покрытие primary/helper WFP — см. [docs/KNOWN_LIMITATIONS.md](docs/KNOWN_LIMITATIONS.md).

Предыдущая portable-веха: тег `portable-bootstrap-pass-v1` (коммит `0870c387`).

### Подтверждено сейчас (TCP IPv4)

- Connect-redirect (WFP) по процессам для выбранных `.exe` (ASCII и Unicode пути)
- Изоляция Probe + исходящий трафик curl реального приложения + исходящий Chrome + Telegram (Unicode путь)
- WFP ALE APP_ID, callout перенаправления, прокси, контекст перенаправления, исходящий через VPN
- История потоков реального приложения с сопоставлением цели (фоновые потоки не дают ложный успех цели)

### Ещё не подтверждено (не предполагайте поддержку)

- Подключение **Work/корпоративного VPN** из приложения (экспериментальный код; скрыто, пока не задано `VPN_ROUTE_ENABLE_WORK_VPN=1`). Для корпоративного VPN используйте **OpenVPN GUI**; VPN Route отвечает только за выборочный VPN для приложений.
- Полная маршрутизация UDP / QUIC по процессам
- Сквозной IPv6-исходящий трафик через VPN
- Произвольная DNS-маршрутизация для всех приложений

### В работе — portable-поставка / bootstrap V1

Для конечного пользователя — **самодостаточный ZIP win-x64** (без отдельной установки .NET 10 Runtime). Служба Windows **+ драйвер WFP** регистрируются один раз через повышенный `SelectiveVpnRouter.Bootstrap.exe`; после состояния bootstrap **Ready** приложение запускается без повышения прав.

Полное руководство: [docs/portable-distribution.md](docs/portable-distribution.md)

**Сборка portable-пакета** (из корня репозитория):

```powershell
.\scripts\build-portable.ps1
.\scripts\test-portable-preflight.ps1
```

Результат: `artifacts\portable\VPN-Route-<version>-x64.zip` и соответствующая папка.

**Первый запуск (распаковать куда угодно, например `C:\Tools\VPN Route\`)**

1. Запустите `SelectiveVpnRouter.App.exe` — UAC не нужен, если служба/драйвер уже соответствуют этой папке.
2. Если появится overlay, выберите **Подготовить** / **Обновить** → UAC → `SelectiveVpnRouter.Bootstrap.exe repair`.
3. После **Ready** при необходимости укажите путь **OpenVPN Community** в настройках (не входит в комплект).

**Обновление:** распакуйте новый ZIP поверх той же папки (или в новую) и выполните **Обновить** при `VersionMismatch` / переносе.

**Перенос папки:** скопируйте каталог целиком; запустите App → **NeedsRepair** → **Обновить** (перепривязка `ImagePath` службы).

**Удаление системных компонентов (конфиг сохраняется):** повышенный `SelectiveVpnRouter.Bootstrap.exe remove --root "<portable dir>"` — сохраняет `%ProgramData%\SelectiveVpnRouter\config.json` и `%LocalAppData%\SelectiveVpnRouter\ui.json`.

Развёртывание для разработки (публикация с framework-dependent) по-прежнему: `scripts\update-desktop.ps1` → `artifacts\publish`.


## Установка/обновление для разработчика (VPN Route + расширение браузера)

**Slice 10A — ПРИНЯТО / ЗАВЕРШЕНО:** единый сценарий установки/обновления для разработчика.

**Каноническая команда** (повышенный Windows PowerShell 5.1 из этого репозитория):

```powershell
cd "E:\Работа\OSPanel\domains\vpn-gateway"
powershell -ExecutionPolicy Bypass -File .\scripts\install-vpn-route.ps1
```

Аудит только чтения (без сборок, без развёртывания Service, без записи в реестр, без записей BrowserRouting):

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-vpn-route.ps1 -CheckOnly
```

Порядок: **сначала сборка/тесты репозитория расширения** → `update-desktop.ps1` (Service/App) → регистрация штатного Native Host (`com.vpnroute.browser`) → проверка интеграции только чтения → вывод пути к `ext-vpn-route\dist\extension`.

Параметры:

- `-ExtensionRepoPath` — путь к соседнему `ext-vpn-route` (по умолчанию: `..\ext-vpn-route` или `EXT_VPN_ROUTE_ROOT`)
- `-CheckOnly` — только статус; см. команду выше
- `-VerboseOutput` — строки «ПРОЙДЕНО» в консоли (полная детализация в `artifacts/logs/install-vpn-route-*.log`)

**Перед полной установкой/обновлением:** закройте **SelectiveVpnRouter.App** (GUI). Оркестратор предупреждает, если приложение открыто; `update-desktop.ps1` останавливает runtime VPN Route на время публикации.

Установщик **не** перезагружает расширение Chromium/Yandex. После успеха перезагрузите на `browser://extensions` или загрузите распакованное расширение из напечатанной папки `dist\extension` (штатный ID `lfaekfalhkgmbfdjjlfcalanhijeaien`).

Проверка после установки — **только чтение** (ping Native Host + манифест интеграции); RPC записи правил не вызываются.

Сбои **до** первого запуска `update-desktop.ps1` выводят сообщения `INSTALL FAILED BEFORE UPDATE` / `No live changes were made.` Сбои после публикации Service — `Partial update...`.

**Проверка парсера (Windows PowerShell 5.1):** `scripts\validate-install-vpn-route-parse.ps1` (также в тестах оркестратора ext-vpn-route).

Yandex Browser использует ключ реестра Native Messaging **Chrome** (`HKCU\Software\Google\Chrome\NativeMessagingHosts\com.vpnroute.browser`) согласно документам приёмки ext-vpn-route.

Этот оркестратор **не** запускает, не останавливает и не завершает **внешние** процессы OpenVPN.

**Историческая базовая линия на момент приёмки Slice 10A (полный путь установки):** ext-vpn-route **552** Node + **219** Native Host; vpn-gateway **823** через `update-desktop.ps1`. Актуальные значения — в разделе «Актуальная базовая линия тестов» выше и в README ext-vpn-route.

### Только Service/App (устаревший шаг)

```powershell
.\scripts\update-desktop.ps1
```

Выполняет **сборку → тесты → публикацию → перезапуск службы → дымовой прогон среды выполнения**. Лог: `artifacts/logs/update-desktop-*.log`.

Подробный вывод: `.\scripts\update-desktop.ps1 -VerboseOutput`

## Состав решения

| Компонент | Роль |
| --- | --- |
| `SelectiveVpnRouter.App` | WPF GUI без повышения прав, значок в трее, Test Center |
| `SelectiveVpnRouter.Service` | Привилегированная служба (Windows Service или `--console`) |
| `SelectiveVpnRouter.Probe` | Автономная проверка маршрута |
| `SelectiveVpn.V0` | Ранний прототип транспорта (PoC; сохранён; не удалять) |
| `driver/SelectiveVpnCallout` | Минимальный KMDF WFP callout connect-redirect (нужен для *прозрачного* TCP по процессам) |

Постоянный конфиг: `%ProgramData%\SelectiveVpnRouter\config.json`
Настройки UI: `%LocalAppData%\SelectiveVpnRouter\`

## Cursor через VPN, Git — напрямую

1. Установите [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) и [OpenVPN Community](https://openvpn.net/community-downloads/) (`openvpn.exe`, не OpenVPN 3).
2. Сборка:

```powershell
cd E:\Работа\OSPanel\domains\vpn-gateway
dotnet build SelectiveVpnRouter.sln -c Release
dotnet test SelectiveVpnRouter.sln -c Release
```

3. Для обычного сценария настольного приложения см. **[Запуск desktop](docs/DESKTOP_LAUNCH.md)** — один раз публикация, один раз установить службу Windows, затем двойной щелчок по `SelectiveVpnRouter.App.exe`.

   Консольный режим разработки (с повышением прав): `SelectiveVpnRouter.Service.exe --console`

   Или установка службы: `powershell -ExecutionPolicy Bypass -File scripts\publish-desktop.ps1`, затем `scripts\install-service.ps1`

4. Запустите `SelectiveVpnRouter.App.exe` (повышение прав не требуется).
5. Мастер настройки: выберите `openvpn.exe`, ваш `.ovpn`, при необходимости **режим совместимости (отключить DCO)**, если DCO конфликтует с другим VPN-драйвером.
6. Подключитесь. Личный туннель запускается с `--route-nopull`, чтобы **не** заменять системный маршрут по умолчанию.
7. Добавьте правила:
   - Приложение `Cursor.exe` → **VPN**
   - Приложение `git.exe` → **DIRECT** (совпадение по имени файла; не наследует от Cursor)
8. По желанию: добавьте `SelectiveVpnRouter.Probe.exe` → VPN, затем:

```powershell
.\SelectiveVpnRouter.Probe.exe --http https://api.ipify.org
```

Вторая копия Probe **без** VPN-правила приложения должна оставаться Direct.

**Прозрачный** TCP `Cursor.exe` → VPN требует callout-драйвер WFP (см. [docs/DRIVER_SETUP.md](docs/DRIVER_SETUP.md)). Пока драйвер не загружен, Test Center сообщает об этом честно; `Probe --via-proxy 127.0.0.1:<proxyPort>` по-прежнему доказывает путь через прокси, привязанный к VPN-сессии.

## Режимы маршрутизации (как есть)

- **Приложение** — настоящий TCP по процессам (WFP ALE APP_ID). `git.exe`, запущенный из Cursor, остаётся `git.exe`.
- **Домен / IP / CIDR** — **глобально для этого назначения** (все процессы). UI это явно показывает. Это *не* маршрутизация по процессам.

## Открытый отказ (fail-open)

Если GUI, служба, OpenVPN или политика драйвера пропадают, интернет в режиме Direct должен продолжать работать. **EMERGENCY RESTORE** снимает только маршруты, фильтры WFP и управляемый OpenVPN *этого приложения*.

## Browser Routing 1.0.0 RC19

VPN Route Service отдаёт авторитетное состояние Browser Routing через IPC-канал только для чтения; в **1.0.0 RC19** добавлен канал push-событий (`SelectiveVpnRouter.BrowserRouting.Events`), чтобы расширение Chromium синхронизировалось после зафиксированных изменений правил, с периодическим опросом `alarm` как запасной вариант. Настольное приложение остаётся основным редактором правил; расширение не поднимает VPN и применяет PAC из состояния Service через явный локальный SOCKS5.

## Документация

- [Запуск desktop](docs/DESKTOP_LAUNCH.md)
- [Архитектура](docs/ARCHITECTURE.md)
- [Исследование сети](docs/NETWORK_ARCHITECTURE_RESEARCH.md)
- [План ручного тестирования](docs/MANUAL_TEST_PLAN.md)
- [Настройка драйвера](docs/DRIVER_SETUP.md)
- [Подпись драйвера](docs/DRIVER_SIGNING.md)
- [Устранение неполадок](docs/TROUBLESHOOTING.md)
- [Безопасность](docs/SECURITY.md)
- [Известные ограничения](docs/KNOWN_LIMITATIONS.md)
- [Прототип транспорта PoC V0](docs/V0.md)
- [Как запустить V0](docs/HOW_TO_RUN.md)
