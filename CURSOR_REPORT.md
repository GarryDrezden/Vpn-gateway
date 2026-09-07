# CURSOR_REPORT — V0 / V0.1

Краткий отчёт для следующего обсуждения архитектуры.

## Фактический результат V0 (пользователь, Windows 11 + OpenVPN 2.6.6)

```
RESULT: PARTIAL
Direct internet: OK
VPN tunnel: OK
System default route unchanged: YES
System DNS unchanged: YES
IP_UNICAST_IF applied: YES
Internet through selected VPN interface: NO
WSA 10051 / WSAENETUNREACH
```

**Вывод V0:** OpenVPN-туннель успешно существует параллельно Direct. Default route и DNS не меняются. Одного `IP_UNICAST_IF` недостаточно: через VPN interface нет маршрута до произвольного Internet destination.

## Что реализовано сейчас (V0.1 код)

Расширение того же `SelectiveVpn.V0`:

* до OpenVPN: DNS resolve public-IP hostname → один IPv4 TEST_IP → Direct HTTPS baseline на этот IP (SNI = hostname);
* OpenVPN CLI: `--config` `--route-nopull` `--route TEST_IP 255.255.255.255 vpn_gateway` `--verb 3`;
* `.ovpn` не меняется;
* после туннеля: поиск TEST_IP/32 в `GetIpForwardTable`; abort если появились 0.0.0.0/0, 0.0.0.0/1, 128.0.0.0/1;
* Direct control на **другой** IPv4, не TEST_IP;
* главный VPN-тест: TCP/TLS на TEST_IP **без** `IP_UNICAST_IF`;
* cleanup: проверить исчезновение /32; аварийно удалить **только** этот точный /32 через `DeleteIpForwardEntry`, если он однозначно наш;
* код `IP_UNICAST_IF` оставлен как V0 diagnostic, в главном тесте V0.1 не используется.

PASS V0.1 **не заявлялся** — нет нового пользовательского прогона.

## API (официальные источники)

* OpenVPN 2.6 `vpn-network-options.rst`: `--route`, `vpn_gateway`, teardown «in reverse order prior to TUN/TAP device close»; `--route-nopull` фильтрует только pull/push.
* Microsoft Learn: `GetIpForwardTable` / `MIB_IPFORWARDROW`; `DeleteIpForwardEntry` (emergency /32 only, proto `MIB_IPPROTO_NETMGMT`).
* V0 Winsock `IP_UNICAST_IF` — без изменений.

## Известные ограничения V0.1

* Если OpenVPN не выведет `vpn_gateway` (нет ifconfig/route-gateway) — ожидаемый PARTIAL, не костыль default route.
* `/32` — не модель application routing.
* DNS по-прежнему системный.
* Emergency delete только при уникальном совпадении dest+mask+ifIndex+nextHop с наблюдавшимся рядом.

## Для решения о V1

Сначала нужен пользовательский RESULT V0.1 (PASS или честный PARTIAL/FAIL). V1 не начинать по этому отчёту.
