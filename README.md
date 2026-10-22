# GitLab CI/CD Notify

Windows aplikácia v **.NET 10 + WPF**, ktorá po spustení zostane v systémovej oblasti (systray). Menu má položky **Stav**, **Nastavenia** a **Ukončiť**. Dvojklik na ikonu otvorí stav. Zatvorenie okien ponechá sledovanie na pozadí; aplikácia sa ukončí cez menu.

## Spustenie

Potrebujete Windows 10 2004 alebo novší / Windows 11 a [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0). Pre vývoj potrebujete .NET 10 SDK.

```powershell
dotnet build GitlabCICDNotify.slnx -c Release
dotnet run --project src/GitlabCICDNotify.App -c Release
```

Lokálny výstup pripravený na spustenie je v `artifacts/app/GitlabCICDNotify.exe`. Zachovajte celý obsah tohto priečinka. Dá sa opätovne vytvoriť:

```powershell
dotnet publish src/GitlabCICDNotify.App -c Release -o artifacts/app
```

Pre otvorenie konkrétneho okna pri vývoji môžete pridať `-- --status` alebo `-- --settings` k `dotnet run`. Bežný štart nezobrazuje okno. Automatické spúšťanie po prihlásení do Windows sa nenastavuje.

## Prvé prihlásenie

1. Cez pravý klik na ikonu otvorte **Nastavenia**.
2. Zadajte HTTPS adresu GitLabu, napr. `https://gitlab.com` alebo `https://gitlab.firma.sk/gitlab`.
3. Kliknite na **Vytvoriť token v prehliadači**. Na GitLabe sa prihláste požadovaným účtom, vytvorte osobný token s predvyplneným oprávnením **read_api** a skopírujte ho. Ak staršia verzia GitLabu nepredvyplní formulár, vyberte `read_api` ručne.
4. Vložte token a zvoľte **Overiť a pripojiť účet**. Meno používateľa sa načíta automaticky.

Postup môžete opakovať pre ďalšie inštancie aj ďalšie účty na rovnakej inštancii. Zhodná adresa inštancie a GitLab user ID aktualizujú existujúci token. Po jeho expirácii použite rovnaký postup. Pri druhom účte prepnite prihlásenie v prehliadači na požadovaného používateľa.

Používa sa osobný token, pretože univerzálne OAuth prihlásenie do ľubovoľnej vlastnej inštancie vyžaduje najskôr zaregistrovanú OAuth aplikáciu na danom serveri. Aplikácia tak nevyžaduje administrátorské nastavenie integrácie.

## Sledovanie

- V **Nastaveniach → príslušný účet → Sledovanie účtu** vyberte režim a kliknite na **Uložiť**. Voľba sa ukladá samostatne pre dvojicu GitLab používateľ + inštancia. Viacerí používatelia na rovnakom serveri aj rovnaký používateľ na rôznych serveroch môžu mať odlišné režimy. Nový účet vždy začína predvoleným režimom; obnovenie jeho tokenu zachová existujúcu voľbu. Doteraz uložené voľby zostávajú pri príslušných účtoch zachované.
- **Používateľa, ktorý spustil pipeline** je predvolený režim, aj pri načítaní staršieho súboru účtov. Sledujú sa pipelines spustené prihláseným účtom bez ohľadu na autora commitu a zdroj spustenia, teda aj ručne spustené pipelines.
- Na **GitLabe 19.3+** sa každých 30 sekúnd použije globálny `GET /api/v4/pipelines?created_after=…`. Keď nie sú nové pipelines ani rozbehnuté pipelines, stačí jeden HTTP dotaz na účet. Projekty a commity sa pri tomto sledovaní nenačítavajú. Ďalšie stránky sa čítajú cez kurzorové odkazy `Link`; meno projektu sa na verzii 19.3 odvodí z URL bez dodatočného API dotazu.
- Už zachytené rozbehnuté pipelines sa uložia a kontrolujú jednotlivo aj po opustení časového okna pre nové pipelines a po reštarte. Po skončení alebo odstránení pipeline sa tieto dodatočné kontroly zastavia. Neskoršie opätovné spustenie už ukončenej pipeline s rovnakým ID a pôvodným časom vytvorenia globálny režim nemusí zachytiť.
- Ak globálny endpoint vráti `404`, použije sa kompatibilný projektový endpoint s filtrom `username` na serveri. Nepodporovaný globálny endpoint sa overí iba raz za inštanciu počas behu aplikácie. Chyby autentifikácie, oprávnení a rate limitu sa za chýbajúci endpoint nepovažujú.
- **Autora commitu** zachováva pôvodné sledovanie: pre nearchivované členské projekty načíta zmenené pipelines so zdrojom `push` alebo `merge_request_event` a overí `author_email` commitu na SHA pipeline proti primárnemu/commit e-mailu a potvrdeným sekundárnym e-mailom používateľa. Git commit musí používať e-mail priradený k účtu. Po zmene e-mailov obnovte prihlásenie.
- Pri projektovom sledovaní sa zoznam projektov uchováva 10 minút v pamäti, takže sa nestiahne pri každej kontrole. Nové členstvo alebo nový projekt sa zachytí po obnovení tejto cache. Obnovenie prihlásenia alebo zmena režimu cache príslušných účtov vyprázdni.
- Zmena režimu platí od času uloženia: pre zvolený účet sa obnoví kontrolný bod a vyčistí jeho aktuálny prehľad, aby sa neposielali historické notifikácie podľa nového pravidla. Ostatné účty pokračujú bez zmeny. Nastavenie sa uloží šifrovane spolu s účtom.
- Pri pridaní účtu sa začne sledovanie od aktuálneho času, takže sa neoznámi stará história. Po reštarte sa pokračuje od poslednej úspešnej kontroly vrátane času, keď aplikácia nebežala. Globálne API poskytuje iba nedávne pipelines; po dlhšom vypnutí nemôže garantovať kompletnú históriu.
- Dvojminútový presah kontrol a uložené ID oznámených pipelines zabraňujú opakovaniu notifikácií. Pri prerušení procesu presne medzi odoslaním notifikácie a uložením súboru môže dôjsť k opakovaniu jednej notifikácie.
- Windows notifikácia má tlačidlo **Otvoriť pipeline**. Tlačidlo aj klik na telo používajú HTTPS protocol activation, takže sa pipeline otvorí v predvolenom prehliadači aj po ukončení aplikácie.
- Stavové okno zobrazuje posledných 100 zachytených pipelines v aktuálnej relácii, farebný stav a súhrn počtov. Po reštarte sa prehľad znovu napĺňa výsledkami API. Diagnostické logy sú predvolene zbalené a držia posledných 200 správ v pamäti.
- Pri výpadku sa neposunie kontrolný bod. Ďalší pokus používa predlžujúci sa interval do 15 minút, s rešpektovaním dlhšieho `Retry-After`. Účty sa spracúvajú nezávisle; chyba jedného neodpojí ostatné.

V režime autora sa sleduje autor vrcholového commitu pipeline. Push viacerých commitov s cudzím vrcholovým commitom, ručné/plánované/downstream pipelines a syntetické merge commity s iným autorom sa nepriraďujú podľa autorov predchádzajúcich commitov. Projektové sledovanie neprehľadáva verejné projekty mimo členstva účtu. Globálne API vracia pipelines používateľa naprieč projektmi, ku ktorým má prístup; štandardne nezahŕňa child pipelines. Ak účet nemá prístup k CI/CD API projektu, stav zobrazí chybu oprávnenia.

## Uloženie a bezpečnosť

Účty, tokeny, kontrolné body a identifikátory oznámených pipelines sú v `%LOCALAPPDATA%\GitlabCICDNotify\accounts.dat`. Celý súbor je šifrovaný cez **Windows DPAPI / CurrentUser**, s atomickou náhradou pri zápise. Dá sa dešifrovať iba v príslušnom Windows používateľskom profile. Tokeny sa nevypisujú do diagnostických logov.

Odpojenie odstráni účet a jeho údaje z lokálneho súboru; samotný token môžete zrušiť v GitLabe. Poškodený alebo nedešifrovateľný súbor sa neprepisuje. Na obnovenie použite zálohu alebo premenujte súbor a po reštarte sa prihláste znova.

Požadované je HTTPS s platným certifikátom. HTTP presmerovania API sa nenasledujú, aby sa token neposlal na iný server. Odkazy pipelines musia smerovať na rovnaký HTTPS origin ako prihlásený GitLab. Na zobrazenie oznámení musia byť povolené Windows notifikácie; režim Nerušiť môže potlačiť banner.

Spúšťajte jednu kópiu aplikácie na používateľský profil; súbor účtov nie je určený na súbežné zápisy z viacerých procesov.

## Overenie

```powershell
dotnet run --project tests/GitlabCICDNotify.Checks -c Release
dotnet run --project tests/GitlabCICDNotify.Checks -c Release -- --desktop
```

Kontrolný program bez testovacieho frameworku preveruje autentifikáciu, oba režimy, predvolený režim starších aj nových účtov, nezávislé voľby používateľov na rovnakej inštancii aj používateľa na rôznych inštanciách a zachovanie voľby pri obnovení tokenu a reštarte. Overuje globálne kurzorové stránkovanie, bezpečnosť odkazov stránkovania, fallback starších verzií, cache projektov, úsporné kontroly a aktualizácie rozbehnutých pipelines. Overuje aj autora commitu, spracovanie chýb/429, bezpečnosť URL, toast XML, Windows DPAPI, viac účtov, obnovu po reštarte, deduplikáciu a ochranu poškodeného úložiska. Používa simulované HTTP odpovede a dočasné úložisko, nevyžaduje skutočný token a neposiela testovacie notifikácie.

Parameter `--render` vykreslí obe okná s ukážkovými údajmi do `artifacts/previews`. `--desktop` pridá kontrolu skrytia/opätovného otvorenia okna a živý test HMENU: natívny `GUI_INMENUMODE`, správny owner HWND, zrušenie menu a odložené vykonanie zvolenej akcie. Testy DPAPI a desktop kontroly potrebujú prístup k používateľskému profilu a interaktívnej Windows relácii.

Manuálne zostáva overiť prihlásenie do reálnej inštancie a doručenie/kliknutie na Windows notifikáciu, menu z panela skrytých ikon, presuny medzi monitormi s rôznym DPI a klávesovú aktiváciu samotnej tray ikony. Automatické kontroly tieto vlastnosti shellu nepotvrdzujú.

## Štruktúra

- `src/GitlabCICDNotify.Core`: modely, validácia URL, stránkovaný GitLab REST klient a priradenie commitov.
- `src/GitlabCICDNotify.App`: WPF okná, koordinácia monitorovania, DPAPI úložisko, Windows toast a WinForms NotifyIcon s natívnym owner-drawn HMENU.
- `tests/GitlabCICDNotify.Checks`: funkčné a desktop kontroly.

Implementácia vychádza z [GitLab Pipelines API](https://docs.gitlab.com/api/pipelines/), [Commits API](https://docs.gitlab.com/api/commits/), [predvyplnenia osobného tokenu](https://docs.gitlab.com/user/profile/personal_access_tokens/) a [ToastNotificationManagerCompat](https://learn.microsoft.com/en-us/dotnet/api/communitytoolkit.winui.notifications.toastnotificationmanagercompat).
