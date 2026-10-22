# Technické a návrhové rozhodnutia

Tento dokument zachytáva dôvody súčasnej implementácie; nejde o zoznam plánovaných funkcií.

## Režim patrí dvojici používateľ + inštancia

Rozlišujeme autora commitu a spúšťajúceho používateľa. Každý účet má vlastný `MonitoringMode`; nastavenia sa nezdieľajú medzi používateľmi rovnakého servera ani medzi servermi. Nový účet aj starší JSON bez tohto poľa použijú `TriggeringUser = 0`. Zmenu režimu aplikujeme od aktuálneho času, aby nevyvolala historické oznámenia.

## Predvolená stratégia minimalizuje sieťové požiadavky

Globálne API zavedené v GitLabe 19.3 vyhľadáva pipelines spustené prihláseným účtom naprieč projektmi. Pri nečinnosti stačí jedna požiadavka na účet a scan. Nepotrebuje zisťovať autora commitu. Samostatné kontroly už aktívnych pipelines kompenzujú chýbajúci filter `updated_after` globálneho zoznamu.

Podporu zisťujeme pokusom o globálny zoznam, nie požiadavkou na verziu servera. `404` zapne projektový fallback; `401`, `403` a `429` sa nesmú považovať za dôkaz staršej verzie. Projektová cache obmedzuje opakované prenosy pri fallbacku aj režime autora.

Používa sa polling; WebSocket, SSE, webhook prijímač a ETag podmienené požiadavky implementované nie sú. Pri návrhu boli posúdené GraphQL subscriptions na stav už známej pipeline a webhooks vyžadujúce dostupný prijímací endpoint a nastavenie projektu. Samostatná desktop aplikácia má fungovať po prihlásení bez prevádzky servera. Podporu API na konkrétnej verzii treba pri ďalšom rozširovaní znovu overiť.

## Autentifikácia bez registrácie integrácie

Používateľ zadá HTTPS URL a osobný token `read_api`. Prehliadač otvorí GitLab formulár s predvyplneným názvom a scope. Identita sa zisťuje cez `/user`, potvrdené e-maily cez `/user/emails`; prístup k API sa overuje aj malým dotazom na projekty. Nevyžadujeme registráciu OAuth aplikácie na každej vlastnej inštancii. OAuth ani automatická obnova tokenu nie sú implementované.

Token sa posiela iba v `PRIVATE-TOKEN` hlavičke. URL inštancie podporuje inštaláciu pod podcestou. API redirecty sú vypnuté. Pipeline odkaz musí mať rovnaký HTTPS origin (schéma, host, port); kurzorový odkaz navyše presnú cestu globálneho endpointu. Origin kontrola sama neobmedzuje pipeline URL na rovnakú podcestu.

## Lokálne úložisko a doručovanie

Celý stav účtov šifruje Windows DPAPI `CurrentUser`; nie je tu serverová databáza ani vlastná správa šifrovacieho kľúča. Predvolené hodnoty nových polí umožňujú načítať staršie uložené účty. Po zlyhaní načítania koordinátor blokuje pridanie účtu, aby sa pôvodný súbor neprepísal.

Oznámené ID sa ukladajú po odoslaní toastu. Deduplikácia pokrýva opakované scany a bežný reštart, ale nie je to garancia exactly-once: pád medzi toastom a zápisom môže zopakovať oznámenie. Checkpoint sa posúva až po úspešnom spracovaní; história `Notified` sa zatiaľ nečistí.

## Natívna Windows integrácia, malá aplikačná vrstva

WinForms `NotifyIcon` zabezpečuje ikonu, natívny HMENU skutočnú Windows menu slučku aj pri skrytých WPF oknách. WPF popup ani `ContextMenuStrip` nie sú produkčným tray menu. Trvalý owner a odložená akcia po skončení menu chránia otvorenie okna a ukončenie aplikácie pred reentranciou.

Windows toast používa `CommunityToolkit.WinUI.Notifications` 7.1.2. Protocol activation necháva otvorenie URL na shelli a predvolenom prehliadači; aplikácia nemusí bežať pri kliknutí. UI používa spoločné XAML resources, binding, code-behind a ručne zostavené služby; nebol zavedený MVVM/DI framework.

## Referencie pri rozširovaní

- GitLab: [Pipelines API](https://docs.gitlab.com/api/pipelines/), [Commits API](https://docs.gitlab.com/api/commits/), [osobné tokeny a predvyplnenie formulára](https://docs.gitlab.com/user/profile/personal_access_tokens/).
- Windows: [ToastNotificationManagerCompat](https://learn.microsoft.com/en-us/dotnet/api/communitytoolkit.winui.notifications.toastnotificationmanagercompat), [TrackPopupMenuEx](https://learn.microsoft.com/en-us/windows/win32/api/winuser/nf-winuser-trackpopupmenuex).
