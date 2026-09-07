# Как запустить V0

Это инструкция для запуска **на вашем Windows-компьютере**. Разбирать исходники не нужно. V0 не ставит драйверы, не меняет `.ovpn` и не добавляет системные маршруты.

## 1. Что должно быть установлено

* Windows 11 (x64);
* [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0) (LTS);
* [OpenVPN Community](https://openvpn.net/community-downloads/) — обычный `openvpn.exe`, не OpenVPN3;
* рабочий `.ovpn`, который **уже умеет подключаться** без интерактивного ввода пароля в этом PoC (сертификаты / `auth-user-pass` с файлом).

Если профиль спрашивает username/password или пароль ключа в консоли — V0 остановится с `AUTH INTERACTION REQUIRED`. Это ожидаемо.

## 2. Как проверить .NET

Откройте обычный PowerShell и выполните:

```powershell
dotnet --version
```

Ожидается версия **10.x** (например `10.0.400`). Если команда не находится или версия 8/9 — установите SDK 10 по ссылке выше и **откройте новое** окно PowerShell.

Проверка, что проект видит SDK:

```powershell
dotnet --list-sdks
```

В списке должна быть строка, начинающаяся с `10.`.

## 3. Как найти openvpn.exe

Типичный путь:

```text
C:\Program Files\OpenVPN\bin\openvpn.exe
```

Он может отличаться (другой диск, установка «только для пользователя»). Проверка:

```powershell
Test-Path "C:\Program Files\OpenVPN\bin\openvpn.exe"
& "C:\Program Files\OpenVPN\bin\openvpn.exe" --version
```

`--version` часто печатает версию и завершается с кодом 1 — это нормально для OpenVPN. Нужна строка вроде `OpenVPN 2.7.x`.

Если файла нет — поставьте OpenVPN Community и посмотрите путь в свойствах ярлыка GUI.

## 4. Куда положить .ovpn

**Не кладите `.ovpn`, `.key`, `.pem`, `.crt` в репозиторий.** Git их игнорирует специально.

Оставьте профиль там, где он уже лежит (например `D:\VPN\my-profile.ovpn`).

Если рядом лежат `.crt` / `.key` / `.pem` и в профиле на них **относительные** пути — не разносите файлы по разным папкам. V0 запускает OpenVPN с рабочей директорией = папка профиля, чтобы относительные пути продолжили работать.

В самом `.ovpn` не должно быть локальных директив, которые меняют маршрутизацию/DNS:

* `redirect-gateway`
* `redirect-private`
* `route`
* `route-ipv6`
* `dhcp-option`
* `dns`
* `block-outside-dns`

Серверные push-маршруты V0 отсекает командной строкой `--route-nopull`, **не** правкой файла. Если такие строки уже прописаны у вас локально — V0 откажется запускаться. Это защита, не баг.

## 5. Как открыть PowerShell

**Открой PowerShell от имени администратора.**

Пуск → введите `PowerShell` → правая кнопка → **Запуск от имени администратора** → Да в UAC.

Без прав администратора OpenVPN обычно не поднимает туннель. V0 сам себя с UAC не перезапускает.

## 6. Как перейти в проект

Подставьте путь, куда склонирован репозиторий. На этой машине это может быть:

```powershell
cd "E:\Работа\OSPanel\domains\vpn-gateway"
```

Если проект лежит в другом месте:

```powershell
cd "D:\Projects\SelectiveVpnRouter"
```

Проверка: в папке есть `SelectiveVpnRouter.sln` и каталог `src`.

## 7. Как собрать

```powershell
dotnet restore
dotnet build
```

Ожидается `Build succeeded` без ошибок. Предупреждения, если появятся, можно прислать вместе с результатом прогона.

## 8. Как запустить

Готовая команда для PowerShell (**одна строка**). Замените только путь к `.ovpn`, если OpenVPN установлен в стандартном месте:

```powershell
dotnet run --project ".\src\SelectiveVpn.V0\SelectiveVpn.V0.csproj" -- --openvpn "C:\Program Files\OpenVPN\bin\openvpn.exe" --profile "D:\VPN\my-profile.ovpn"
```

Пример с таймаутом 45 секунд:

```powershell
dotnet run --project ".\src\SelectiveVpn.V0\SelectiveVpn.V0.csproj" -- --openvpn "C:\Program Files\OpenVPN\bin\openvpn.exe" --profile "D:\VPN\my-profile.ovpn" --timeout 45
```

Кавычки вокруг путей с пробелами обязательны.

## 9. Что должно появиться

Сначала шаги `[1/9] … [9/9]`, затем блок `RESULT`.

Нормальный **PASS** (сокет через VPN реально вышел в интернет):

```text
==================================================
RESULT: PASS
Direct internet: OK
VPN tunnel: OK
System default route unchanged: YES
System DNS unchanged: YES
Socket forced to VPN: YES
==================================================
```

Нормальный **PARTIAL** (туннель жив, Direct не сломан, но интернет через один только выбор интерфейса недоступен — это тоже успешный исход исследования):

```text
==================================================
RESULT: PARTIAL
Direct internet: OK
VPN tunnel: OK
System default route unchanged: YES
System DNS unchanged: YES
IP_UNICAST_IF applied: YES
Internet through selected VPN interface: NO

Conclusion:
Tunnel is up. Windows Direct route is unchanged. IP_UNICAST_IF was applied.
Arbitrary Internet destination is not reachable through the VPN interface with interface selection alone.
No global routes were changed.
==================================================
```

`FAIL` — нельзя безопасно считать эксперимент успешным (например default route или DNS внезапно сменились). V0 сам остановит **свой** процесс OpenVPN.

`INCONCLUSIVE` — данных мало (пароль в консоли, непонятно какой adapter, Ctrl+C).

После завершения V0 пытается остановить только тот `openvpn.exe`, который запустил сам. Чужие OpenVPN GUI/процессы он не убивает.

Если в конце появится крупная надпись `NETWORK STATE DIFF AFTER CLEANUP` — сеть после остановки не совпала со снимком «до». V0 **не** чинит маршруты самостоятельно. Проверьте сеть вручную.

## 10. Что прислать после теста

После запуска пришлите:

1. финальный блок `RESULT`;
2. блок VPN adapter (`[6/9]`);
3. блок Network safety check (`[7/9]`);
4. блок VPN-bound socket (`[9/9]`);
5. OpenVPN error messages, если они были.

**НЕ присылайте:**

* содержимое `.ovpn`;
* приватные ключи;
* пароли;
* auth-token;
* полные сертификаты.

Достаточно безопасных кусков лога, которые V0 и так печатает (пути к exe/профилю можно оставить, содержимое профиля — нет).
