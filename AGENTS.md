# Pokyny pre vývojových agentov

GitLab CI/CD Notify je Windows tray aplikácia v .NET 10 + WPF. UI a dokumentácia sú po slovensky; identifikátory v kóde sú anglické.

## Orientácia

- [Architektúra](doc/architecture.md): komponenty, tok dát a stav.
- [Rozhodnutia](doc/decisions.md): význam účtu, polling, autentifikácia a Windows integrácia.
- [Vývoj a overenie](doc/development.md): príkazy, konvencie a skúsenosti s prostredím.
- [Stav implementácie](doc/status.md): rozsah, obmedzenia a neoverené správanie.
- [README](README.md) slúži používateľom aplikácie.

Pred zásahom prečítaj relevantný dokument a aktuálny kód. Pri zmene správania aktualizuj príslušnú dokumentáciu; plánované funkcie nepopisuj ako implementované.

## Invarianty

- Účet identifikuje normalizovaná URL inštancie + GitLab `UserId`. Lokálne `Account.Id` je stabilný GUID. Režim patrí účtu, nie celej inštancii; predvolené je `TriggeringUser = 0`.
- Zachovaj rozdiel medzi spúšťajúcim používateľom a autorom vrcholového commitu. Nepridávaj načítavanie projektov/commitov do bežnej globálnej kontroly bez dôvodu.
- `Core` nesmie závisieť od WPF/WinForms. Zmeny `Dashboard` kolekcií a tray zdrojov patria UI vláknu. Koordinátor používa asynchrónny gate; neblokuj UI cez `.Wait()`/`.Result`.
- Zatvorenie okna ho skrýva. Ukončenie aplikácie ruší sledovanie a uvoľňuje tray zdroje. Akcie tray menu vykonávaj až po skončení natívnej menu slučky.
- Skutočné tokeny nepatria do logov, fixture dát ani repozitára. Zachovaj DPAPI `CurrentUser`, HTTPS, kontrolu originu odkazov a zákaz presmerovania API. Používateľský `accounts.dat` nepoužívaj na testy.
- Zachovaj načítanie starších účtov s chýbajúcimi novými vlastnosťami. Poškodené úložisko sa nesmie potichu nahradiť prázdnym súborom.

## Dokončenie zmeny

Pri editovaní zdrojových súborov striktne dodržiavaj pravidlá v `.editorconfig`. Po každej ucelenej zmene spusti `dotnet format`.

Spusť overenie primerané zásahu podľa [vývojového návodu](doc/development.md). Pri zmene API/stavu doplň významné kontroly do existujúceho kontrolného programu. Pri zmene UI vykresli a pozri príslušné okno; pri zmene tray protokolu použi aj `--desktop`.

Pri čisto dokumentačnej zmene stačí kontrola faktov, odkazov a diffu. V závere odlíš vykonané kontroly od neovereného živého správania. Generované `bin/`, `obj/`, `.tools/` a `artifacts/` sa necommitujú.
