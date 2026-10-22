---
name: publish-release
description: Priprav novú verziu GitLab CI/CD Notify podľa changelogu a SemVer, commitni vydávané zmeny a vytvor git tag vX.Y.Z. Použi pri požiadavke na vydanie alebo publikovanie verzie tohto projektu.
---

# Vydanie novej verzie

Pracuj z koreňa repozitára. Prečítaj `AGENTS.md`, `doc/development.md`, `CHANGELOG.md`, `build/Prepare-Release.ps1` a `.github/workflows/release.yml`. Cesty v tomto postupe sú relatívne ku koreňu repozitára.

Požiadavka na vydanie oprávňuje upraviť changelog, commitnúť vydávané zmeny a vytvoriť lokálny tag. Dokonči to bez ďalšieho potvrdzovania. Samotné vytvorenie alebo úprava tohto skillu nie je požiadavkou na vydanie. Push vykonaj iba na výslovný pokyn používateľa; push tagu spustí publikovanie cez GitHub Actions.

## Určenie verzie

- Skontroluj `git status --short`, staged aj unstaged diff, históriu a tagy `vX.Y.Z`. Porovnaj poslednú vydanú verziu v changelogu s tagmi a over jej vzťah k aktuálnej vetve. Pri rozpore zisti príčinu; existujúcu verziu ani tag neprepisuj.
- Vydávaj skutočné položky sekcie `Unreleased`. Ak je prázdna alebo chýba, nevytváraj prázdne vydanie. Význam položiek over proti zmenám od posledného vydania; pri nejasnom texte preskúmaj kód a dokumentáciu.
- Ak používateľ zadá konkrétnu verziu alebo major/minor/patch, použi jeho voľbu. Konkrétna verzia musí byť vyššia než posledná vydaná verzia a nesmie už existovať v changelogu ani tagoch.
- Inak zvoľ najvyššie potrebné zvýšenie podľa [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html): **major** pri nekompatibilnej zmene verejného kontraktu, **minor** pri spätne kompatibilnej novej funkcii alebo označení verejnej funkcie za zastaranú, **patch** pri spätne kompatibilných opravách. Pri zmiešaných položkách má major prednosť pred minor a minor pred patch. Nerozhoduj iba podľa nadpisov Added/Changed/Fixed.
- Pri tejto desktop aplikácii posudzuj zdokumentované používateľské správanie, uložené účty a nastavenia a podporované integračné rozhrania. Interný refaktoring sám osebe nie je nekompatibilnou zmenou. Pri údržbe bez novej funkcie či zmeny kontraktu použi patch.
- Zvýšenie minor nuluje patch; zvýšenie major nuluje minor aj patch. Príklady: oprava `1.4.2 → 1.4.3`, nová kompatibilná funkcia `1.4.2 → 1.5.0`, nekompatibilná zmena `1.4.2 → 2.0.0`.
- Bez predchádzajúceho vydania použi predvolene `0.1.0`. SemVer považuje `0.y.z` za úvodný vývoj bez stabilného API; miestna konvencia je patch pre opravy a minor pre nové funkcie aj nekompatibilné zmeny. Prechod na stabilné `1.0.0` urob na pokyn používateľa.
- Workflow podporuje iba stabilné `X.Y.Z` bez úvodných núl a s MSI limitmi `255.255.65535`. Nepoužívaj prerelease ani build metadata. Neplatnú explicitnú verziu potichu nenahrádzaj inou; vysvetli prekážku.

Pred úpravou stručne oznám zvolenú verziu a dôvod zvýšenia. Automatická voľba nepotrebuje potvrdenie.

## Changelog a overenie

1. Premenuj vydávanú sekciu na `## [X.Y.Z] - YYYY-MM-DD` s aktuálnym dátumom používateľa (Europe/Bratislava). Nad ňou ponechaj prázdnu sekciu `## Unreleased` pre ďalší vývoj. Zachovaj položky, slovenské texty, podsekcie a staršie vydania; prípadné porovnávacie odkazy uprav podľa existujúceho formátu.
2. Verziu pre release nastavuje workflow z tagu pomocou `-p:Version` a `-p:ReleaseVersion`; nepridávaj druhý nezávislý zdroj verzie do projektových súborov. Ak sa implementácia odvtedy zmenila, zosúlaď skutočné existujúce zdroje verzie.
3. Spusť `git diff --check` a v PowerShell 7 `./build/Prepare-Release.ps1 -Tag vX.Y.Z`. Pozri `artifacts/release-notes.md`: musí obsahovať iba položky vydávanej verzie. Vygenerované súbory necommituj.
4. Podľa rozsahu vydávaných zmien vykonaj kontroly z `doc/development.md`; už vykonané kontroly rovnakého obsahu z aktuálnej relácie zbytočne neopakuj. Pri čistej úprave changelogu stačí dokumentačná kontrola a validácia release poznámok. Zlyhanie povinnej kontroly oprav pred commitom a tagom; nemožnosť overenia oznám a nevydávaj ako úspešne overenú verziu.

## Commit a tag

- Zahrň implementáciu a dokumentáciu patriacu k vydávaným položkám, ak ešte nie sú commitnuté. Prezri aj už staged súbory; nesúvisiace rozpracované zmeny zachovaj a nezahrň ich do commitu. Použi explicitné cesty pri `git add`, nie plošné `git add .`. Ak rozsah nemožno bezpečne oddeliť, vyžiadaj iba chýbajúce rozhodnutie o rozsahu.
- Skontroluj `git diff --cached` a vytvor commit `git commit -m "Vydanie X.Y.Z"`. Pri chybe commitu skonči pred tagovaním; nepoužívaj amend ani neobchádzaj hooks.
- Až po úspešnom commite vytvor anotovaný tag `git tag -a vX.Y.Z -m "Vydanie X.Y.Z"`. Tag musí ukazovať na release commit obsahujúci správnu sekciu changelogu aj vydávaný kód. Over to cez `git rev-parse HEAD`, `git rev-parse 'vX.Y.Z^{commit}'` a `git show vX.Y.Z:CHANGELOG.md`; pozri konečný `git status --short`.
- Existujúci tag nikdy neposúvaj ani nemaž. Pri prerušení najprv over, čo už vzniklo; nevytváraj duplicitný commit ani ďalšie zvýšenie iba kvôli opakovaniu. Ak tag už správne ukazuje na hotový release commit, oznám tento stav.
- Ak je výslovne žiadaný aj push, over remote a cieľovú vetvu a odošli iba príslušný commit/vetvu a konkrétny tag, ideálne cez `git push --atomic <remote> <branch> vX.Y.Z`. Nepoužívaj force ani `--tags`. Odmietnutie vyrieš bez prepisovania vzdialenej histórie; chybu oznám. Bez potvrdeného výsledku workflow netvrď, že GitHub release je publikovaný.

V závere uveď verziu a dôvod voľby, hash commitu, tag, vykonané kontroly a stav pushu/publikovania. Odlíš lokálne dokončenie od neovereného živého workflow.
