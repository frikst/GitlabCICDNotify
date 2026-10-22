# Architektúra

## Projekty a zodpovednosti

| Projekt / komponent | Zodpovednosť |
| --- | --- |
| `GitlabCICDNotify.Core` (`net10.0`) | Modely, validácia URL a `GitLabClient`; bez desktopových závislostí. |
| `GitlabCICDNotify.App` (`net10.0-windows10.0.19041.0`) | WPF okná, koordinácia, DPAPI, WinForms tray a Windows toast. |
| `GitlabCICDNotify.Checks` | Konzolové kontroly odkazujúce na App; simulované HTTP, dočasné úložisko, voliteľné desktop kontroly. |

- [App.xaml.cs](../src/GitlabCICDNotify.App/App.xaml.cs) ručne zostaví služby. Drží jeden `HttpClient` (timeout 30 s, bez redirectov), cancellation token a úlohu monitorovania. DI kontajner ani generický host sa nepoužívajú.
- [MonitorService](../src/GitlabCICDNotify.App/MonitorService.cs) vlastní účty, gate, retry stav, ukladanie a aktualizáciu prehľadu. Účty kontroluje postupne. Autentifikácia prebieha pred vstupom do gate; zmeny účtov a scan sú serializované. Súbežný ručný poll sa preskočí.
- [GitLabClient](../src/GitlabCICDNotify.Core/GitLabClient.cs) vykonáva REST požiadavky, stránkovanie, výber stratégie a filtrovanie. Cache projektov je podľa lokálneho ID účtu (10 minút), nepodporované globálne API podľa URL inštancie (do konca života klienta).
- [Dashboard](../src/GitlabCICDNotify.App/Dashboard.cs) obsahuje `ObservableCollection` modely bez tokenov. Stavové riadky sú nahrádzané, nastavovacie riadky majú editovateľný `Mode`. UI používa binding a code-behind, nie úplný MVVM framework.
- [StateStore](../src/GitlabCICDNotify.App/StateStore.cs), [NotificationService](../src/GitlabCICDNotify.App/NotificationService.cs) a [TrayService](../src/GitlabCICDNotify.App/TrayService.cs) izolujú Windows mechanizmy. `IPipelineNotifier` umožňuje testy bez skutočných oznámení.

## Tok sledovania

1. Startup vytvorí tray, načíta účty a spustí prvý scan; ďalšie tiky `PeriodicTimer` majú interval 30 s. Bežne sa neotvorí žiadne okno.
2. `TriggeringUser` skúša globálne `pipelines?created_after=…`. Nasleduje `Link rel="next"`, bez offsetovej paginácie. Ak odpoveď neobsahuje projekt, meno sa odvodí z URL pipeline. Chýbajúce aktívne pipelines sa obnovia jednotlivým projektovým GET.
3. Pri `404` globálneho zoznamu sa zapamätá fallback: členské nearchivované projekty a pipelines filtrované cez `username`. `CommitAuthor` používa tie isté projekty, zdroje `push` a `merge_request_event` a overenie `author_email` na SHA pipeline. Projektové zoznamy používajú `page`, `per_page=100`, `X-Next-Page`.
4. Scan používa kontrolný bod s dvojminútovým presahom a odmieta pipelines vytvorené pred `MonitoringSince`. Výsledok vracia čas začiatku kontroly; klient kontrolný bod sám nemení.
5. Koordinátor aktualizuje prehľad. Pre neoznámené ID odošle toast, pridá ID do `Notified` a uloží stav. Po úspešnom spracovaní aktualizuje aktívne pipelines a posunie checkpoint. Zlyhanie účtu nechá checkpoint nezmenený a neukončí kontrolu ďalších účtov.
6. Retry interval je 30 s, 60 s, 120 s, … najviac 15 minút; dlhší `Retry-After` má prednosť. Retry stav je iba v pamäti.

## Identita a perzistencia

Zdrojom pravdy sú [modely](../src/GitlabCICDNotify.Core/Models.cs). Účet sa pri prihlásení vyhľadá cez normalizovanú URL + číselné `UserId`. Pipeline kľúč je `ProjectId:Id`, UI ho dopĺňa o lokálne ID účtu.

`%LOCALAPPDATA%\GitlabCICDNotify\accounts.dat` je DPAPI-šifrovaný JSON `List<Account>` bez explicitnej verzie schémy. Obsahuje token, e-maily, režim, `MonitoringSince`, checkpoint, aktívne pipelines a oznámené ID. Zápis používa susedný `.tmp` súbor a `File.Move(..., overwrite: true)`; nie je tu zámok medzi procesmi ani explicitný flush na disk. Nešifrovaný JSON bajtový buffer sa nuluje; modely vrátane tokenov zostávajú v pamäti.

Obnovenie tokenu mení token/e-maily, zachováva lokálne ID a režim. Zmena režimu obnoví časové hranice, vyprázdni aktívne pipelines a prehľad iba daného účtu; `Notified` zachová. Pridanie, odstránenie a zmena režimu pri chybe zápisu vracajú príslušné pamäťové zmeny späť.

## Desktopový životný cyklus

WPF má `OnExplicitShutdown`; okná sa vytvárajú lazily, opakovane používajú a pri zatvorení skrývajú. Služby a UI bežia v jednom WPF dispatcher kontexte, sieťové čakanie je asynchrónne. Pri presune práce na iné vlákno treba explicitne riešiť návrat k UI.

Tray používa trvalé skryté top-level HWND, owner-drawn HMENU a `TrackPopupMenuEx`. Pred otvorením presunie owner k monitoru kotvy a zistí DPI, potom meria aj kreslí. Po slučke zničí HMENU, odošle `WM_NULL` a až potom vykoná príkaz. Manifest deklaruje PerMonitorV2. Toast telo aj tlačidlo používajú HTTPS protocol activation bez vlastného callback protokolu aplikácie.
