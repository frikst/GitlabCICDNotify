# GitLab CI/CD Notify

Windows aplikácia v .NET 10 + WPF, ktorá na pozadí sleduje GitLab pipelines a zobrazuje Windows notifikácie s tlačidlom **Otvoriť pipeline**.

## Spustenie

Potrebujete Windows 10 2004 alebo novší / Windows 11 a [.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0).

Ak máte pripravený lokálny výstup, spustite `artifacts/app/GitlabCICDNotify.exe`. Zachovajte aj ostatné súbory tohto priečinka. Zostavenie zo zdrojov opisuje [vývojový návod](doc/development.md).

Po spustení aplikácia zostane v systémovej oblasti (systray). Pravým kliknutím otvoríte menu **Stav**, **Nastavenia**, **O aplikácii**, **Ukončiť**; dvojklik otvorí stav. Zatvorenie okna ponechá sledovanie na pozadí. Aplikáciu ukončíte cez menu. Dialóg **O aplikácii** zobrazuje verziu, autora, organizáciu a odkaz na web katedry. Automatické spúšťanie po prihlásení do Windows sa nenastavuje.

## Prvé prihlásenie

1. Otvorte **Nastavenia** z tray menu.
2. Zadajte HTTPS adresu GitLabu, napr. `https://gitlab.com` alebo `https://gitlab.firma.sk/gitlab`.
3. Kliknite na **Vytvoriť token v prehliadači**. Prihláste sa požadovaným GitLab účtom, vytvorte osobný token s oprávnením **read_api** a skopírujte ho. Názov a oprávnenie predvyplníme; na staršom GitLabe ich prípadne nastavte ručne.
4. Vložte token a zvoľte **Overiť a pripojiť účet**. Meno sa načíta automaticky.

Môžete pridať viac inštancií aj viac používateľov na rovnakom serveri. Pre druhého používateľa prepnite účet v prehliadači. Opätovné pripojenie rovnakého používateľa na rovnakej inštancii obnoví token a zachová jeho voľbu sledovania. Pri expirácii tokenu použite rovnaký postup.

## Čo sa sleduje

Pri každom účte v **Nastaveniach → Sledovanie účtu** vyberte režim a stlačte **Uložiť**:

- **Používateľa, ktorý spustil pipeline** — predvolené a úspornejšie sledovanie, vrátane ručne spustených pipelines bez ohľadu na autora commitu. Novšie GitLaby poskytujú spoločný zoznam naprieč projektmi; staršie sa kontrolujú po projektoch.
- **Autora commitu** — sleduje pipelines po pushi alebo zmene merge requestu podľa e-mailu autora vrcholového commitu. Commit musí používať e-mail priradený k účtu. Po zmene e-mailov obnovte prihlásenie.

Voľba platí iba pre daného používateľa na danej inštancii. Pri zmene režimu sa sledovanie tohto účtu začne od aktuálneho času a jeho prehľad sa vyčistí. Nové prihlásenie tiež neoznamuje starú históriu.

Kontrola prebieha každých 30 sekúnd, pri chybe sa interval predĺži. Stavové okno zobrazuje posledných 100 zachytených pipelines relácie, farebné stavy a zbalené diagnostické logy. Klik na stav pipeline aj telo/tlačidlo notifikácie otvorí prehliadač. Windows notifikácie musia byť povolené; režim Nerušiť môže potlačiť banner.

Po reštarte sa sledovanie obnoví, ale globálny zoznam obsahuje iba nedávne pipelines a nezaručuje kompletnú históriu po dlhom vypnutí. Neskorší retry už ukončenej pipeline sa v globálnom režime nemusí zachytiť. Režim autora nevyhodnocuje všetky commity pushu ani autorstvo syntetických merge commitov. Podrobné [hranice implementácie](doc/status.md) sú v dokumentácii.

## Údaje a riešenie problémov

Účty a tokeny sa ukladajú šifrovane cez Windows DPAPI pre váš Windows používateľský profil do `%LOCALAPPDATA%\GitlabCICDNotify\accounts.dat`. Tokeny sa nevypisujú do logov. Používajte HTTPS s platným certifikátom.

**Odpojiť** odstráni účet z aplikácie; token môžete zrušiť v GitLabe. Ak súbor účtov nejde načítať, zostane zachovaný: obnovte zálohu alebo ho premenujte a po reštarte pripojte účty znova. Spúšťajte jednu kópiu aplikácie na Windows profil.

Pri chybe otvorte **Stav → Diagnostické logy** a overte sieť, oprávnenie `read_api`, platnosť tokenu a prístup k CI/CD projektu.

## Vývojová dokumentácia

Začnite v [AGENTS.md](AGENTS.md); odkazy na architektúru, rozhodnutia, postupy a stav implementácie sú v jeho úvode.

## Licencia

Projekt je licencovaný pod GNU General Public License, verzia 3 (GPL-3.0-only). Úplné znenie je v súbore [LICENSE](LICENSE).
