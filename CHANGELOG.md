# Changelog

Podstatné používateľské zmeny aplikácie GitLab CI/CD Notify.

Formát vychádza z [Keep a Changelog 1.1.0](https://keepachangelog.com/en/1.1.0/). Verzie sa riadia [Semantic Versioning 2.0.0](https://semver.org/spec/v2.0.0.html); postup vydania a konvenciu pre úvodný vývoj opisuje [projektový skill publish-release](.agents/skills/publish-release/SKILL.md).

## Unreleased

## [1.1.0] - 2026-10-26

### Added

- Priebeh pipeline priamo v aplikácii cez akciu **Zobraziť** v oznámení alebo stavovom okne. Zobrazuje stages a joby s aktuálnym stavom a živý výstup vybraného jobu s možnosťou označenia a kopírovania; zatvorenie okna zastaví jeho načítavanie.
- Automatické spúšťanie pri prihlásení do Windows, predvolene zapnuté po prvom spustení aplikácie. V nastaveniach ho možno vypnúť pre aktuálny Windows účet.
- Výber témy **Podľa systému / Tmavá / Svetlá** s okamžitým uložením a prepnutím okien aj tray menu bez reštartu. Predvolene sa téma riadi Windows; terminál výstupu jobu zostáva tmavý.

### Changed

- MSI inštalátor má slovenského sprievodcu s potvrdením licencie GPL-3.0 a predvolene zaškrtnutou možnosťou spustiť aplikáciu po úspešnej inštalácii. Pri tichej inštalácii sa aplikácia nespúšťa.

## [1.0.0] - 2026-10-23

### Added

- Sledovanie GitLab CI/CD pipelines na pozadí vo Windows so slovenským rozhraním a ovládaním cez tray menu. Zatvorenie okna ponechá sledovanie spustené.
- Pripojenie viacerých inštancií a používateľov cez osobný token `read_api`, otvorenie formulára na jeho vytvorenie a obnovenie tokenu so zachovaním nastavení účtu.
- Samostatný režim sledovania pre každý účet: predvolene podľa spúšťajúceho používateľa, voliteľne podľa e-mailu autora vrcholového commitu pri pushi alebo zmene merge requestu. Režim autora používa e-maily načítané pri prihlásení.
- Spoločná kontrola pipelines spúšťajúceho používateľa naprieč projektmi s prechodom na členské nearchivované projekty, ak server spoločné API neposkytuje.
- Pravidelná kontrola každých 30 sekúnd a predlžovanie intervalu pri chybe. Zlyhanie jedného účtu nezastaví kontrolu ostatných účtov.
- Windows oznámenia pri prvom zachytení pipeline s možnosťou otvoriť ju v prehliadači.
- Stavové okno s farebným prehľadom posledných 100 pipelines relácie, odkazmi na pipelines, stavom účtov a zbalenými diagnostickými logmi.
- Šifrované uloženie účtov, tokenov a stavu sledovania pre aktuálny Windows profil cez DPAPI a obnova sledovania po reštarte. Nečitateľný súbor účtov sa neprepíše.
- Začiatok sledovania nového účtu alebo zmeneného režimu od aktuálneho času bez oznamovania staršej histórie.
- Dialóg **O aplikácii** s verziou, autorom, organizáciou a odkazom na web katedry.

### Changed

- Nová ikona aplikácie so zvončekom, symbolom spustenia a bodkou oznámenia v systémovej oblasti, oknách a dialógu O aplikácii.

Podrobné obmedzenia sledovania a rozsah overenia uvádza [stav implementácie](doc/status.md).
