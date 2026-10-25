# Aktuálny stav implementácie

## Implementované

- .NET 10 WPF aplikácia štartujúca do tray; štyri položky menu vrátane dialógu O aplikácii s verziou, autorom a organizáciou, dvojklik na stav, skrytie a obnovenie existujúcich okien, explicitné ukončenie a obsluha ukončenia Windows relácie.
- Viac účtov/inštancií cez osobné tokeny v samostatnom okne **Pripojiť účet**, ktoré sa otvára z nastavení. Režim je samostatný pre používateľa + inštanciu; nový účet má `TriggeringUser`, obnovenie tokenu zachová jeho voľbu.
- Globálna stratégia, projektový fallback, režim autora, stránkovanie, cache projektov, podmienené GET cez ETag s obnovením tela a stránkovania pri 304, kontrolné body, retry, aktívne pipelines a perzistentná deduplikácia.
- Toast s URL akciou aj akciou **Zobraziť** na otvorenie WPF priebehu pipeline; rovnaká akcia je v okne Stav. Priebeh obsahuje stage/job strom vrátane trigger jobov, stavové ikony a živý read-only terminál s výberom a kopírovaním. Polling iba pre meniteľné stavy a vybraný log, Range/ETag s fallbackom, finálny odber, backoff, zrušenie pri skrytí a obnovenie okna.
- Farebný prehľad posledných 100 pipelines relácie a predvolene zbalené logy (200 správ v pamäti).
- DPAPI úložisko s ochranou pred prepísaním po chybe načítania. Funkčné kontroly, render všetkých piatich okien a natívne desktop kontroly. Lokálny publish adresár.
- GitHub Actions release workflow pre tagy `vX.Y.Z`: self-contained Windows x64 ZIP, MSI cez WiX a release poznámky zo sekcie verzie v changelogu. Bez spúšťania kontrolného programu a bez dočasných Actions artefaktov.

## Známe hranice

- Globálny zoznam poskytuje len nedávne pipelines; dlhý offline interval nemusí byť celý obnoviteľný. Štandardná požiadavka nezahŕňa child pipelines. Neskorší retry ukončenej pipeline s rovnakým ID a pôvodným `created_at` sa po opustení okna nemusí obnoviť.
- `CommitAuthor` porovnáva len autora commitu na SHA pipeline; neprechádza celý push ani autorov predchádzajúcich commitov. Syntetický merge commit s iným autorom môže zostať mimo výberu. E-maily sa obnovujú pri prihlásení, nie pri každom scane.
- Projektové stratégie sledujú len členské nearchivované projekty. Nové projekty čakajú na obnovu 10-minútovej cache. Projektová `404` sa preskočí; `403` môže ukončiť celý scan účtu, ďalšie účty pokračujú.
- `ActivePipelines` považuje za ukončené `success`, `failed`, `canceled`, `skipped`, `unavailable`; ostatné stavy vrátane `manual` môžu vyvolávať ďalšie kontroly. UI počítadlo aktívnych stavov má vlastný užší zoznam v `Dashboard`.
- Prehľad/logy sa neukladajú ako kompletná história. `Notified` naopak rastie bez retencie. Toast a zápis jeho ID nie sú jedna transakcia; po páde je možné opakovanie.
- Obnovenie prihlásenia aktualizuje token a e-maily, zatiaľ nie uložený `Username`/`Name`; premenovanie používateľa môže ovplyvniť projektový filter `username`.
- Jeden koordinátor serializuje scany aj zmeny účtov; rozsiahly/pomalý scan môže odložiť zápis nastavení. Nie je implementovaný single-instance zámok ani koordinácia zápisov viacerých procesov.
- Úložisko nemá verziu schémy ani automatickú zálohu. Automatická obnova tokenu, autostart, aktualizácie aplikácie, WebSocket/SSE a webhook prijímač nie sú implementované.

## Overenie a zostávajúce živé kontroly

Pri doplnení priebehu pipeline prešli Release build bez upozornení a 237 kontrol vrátane HTTP/state fixture, toast XML, renderu piatich okien, výberu jobu a rušenia/obnovenia živého cyklu bez súbehu. Náhľady priebehu a Stav boli vizuálne skontrolované. Range/ETag trace na reálnom GitLabe a živá Windows foreground aktivácia cez **Zobraziť** (vrátane štartu ukončenej aplikácie) zostávajú neoverené. GitLab nemusí podporovať Range ani ETag na trace; vtedy sa počas behu prenáša celý log. Terminál uchováva koniec logu do 2 Mi znakov, najviac osem jobov na okno; logy sa neukladajú na disk.

Pri doplnení ETag prešli Release build bez upozornení a simulované HTTP/state kontroly: 304 pre kurzorové aj projektové stránkovanie a detaily, zmena stavu, izolácia URL/účtov/tokenov, invalidácia účtu, chyby oprávnení, odpovede bez ETag, no-store a živé overovanie pri prihlásení. ETag komunikácia so skutočnou inštanciou GitLabu zostáva neoverená.

Pri doplnení release workflow prešli lokálny self-contained publish `win-x64`, vytvorenie ZIP, zostavenie MSI vrátane WiX/ICE validácie bez upozornení a náhľad extrakcie jednej verzie z changelogu. Unit testy v projekte nie sú; existujúci konzolový kontrolný program sa pri tejto zmene nespúšťal. Beh workflow na GitHube, publikovanie release, živá MSI inštalácia, upgrade a odinštalovanie zostávajú neoverené.

Počas implementácie prešli Release build bez upozornení, simulované API/state kontroly vrátane DPAPI a nezávislosti režimov účtov, vykreslenie okien a kontrola natívnej menu slučky. Pri poslednej zmene nastavení prešli aj render a hide/reopen/restore kontroly; natívny menu protokol sa nemenil.

Pri doplnení dialógu O aplikácii prešli Release build, 128 kontrol vrátane renderu troch okien, skrytia a opätovného otvorenia dialógu a výberu jeho tray položky až po skončení natívnej menu slučky. Náhľad dialógu bol vizuálne skontrolovaný.

Po presune pripojenia účtu do samostatného okna prešli Release build bez upozornení a 126 kontrol vrátane renderu štyroch okien, skrytia nového okna, vymazania vloženého tokenu a jeho opätovného otvorenia a obnovenia z minimalizácie. Náhľady nastavení a pripojenia účtu boli vizuálne skontrolované.

Reálny GitLab login, doručenie Windows toastu a otvorenie jeho URL neboli overené skutočným účtom. Test toastu kontroluje XML, nie shellové doručenie. Ručne treba overiť menu z panela skrytých ikon, klávesovú aktiváciu tray ikony, rôzne polohy taskbaru, zmiešané DPI, prepínanie kontrastu a Windows logoff. Manifest/renderer obsahujú podporu DPI a high contrast, ale samotný kód nie je dôkaz ich plného správania v shelli.
