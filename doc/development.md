# Vývoj a overenie

## Prostredie a príkazy

Pracuje sa vo Windows s PowerShellom a .NET 10 SDK. `global.json` požaduje 10.0.100 s `latestFeature`; pri implementácii bol dostupný SDK 10.0.402. Solution má formát `.slnx`. Príkazy spúšťaj z koreňa checkoutu.

```powershell
dotnet restore GitlabCICDNotify.slnx --configfile NuGet.Config
dotnet build GitlabCICDNotify.slnx -c Release --no-restore
dotnet run --project src/GitlabCICDNotify.App -c Release --no-build -- --settings
dotnet run --project tests/GitlabCICDNotify.Checks -c Release --no-build
```

`--status` otvorí stavové okno, `--about` dialóg O aplikácii; bez parametra aplikácia zostane v tray. Publish je framework-dependent adresár vyžadujúci .NET 10 Desktop Runtime, nie samostatný inštalátor:

```powershell
dotnet publish src/GitlabCICDNotify.App -c Release -o artifacts/app --no-restore
```

Výstup `artifacts/app/GitlabCICDNotify.exe` potrebuje aj ostatné súbory adresára. `artifacts/`, `bin/`, `obj/`, `.tools/` a lokálne IDE súbory ignoruje Git.

## Konvencie

- Nullable, warnings-as-errors, deterministické zostavenie a `LangVersion=latest` nastavuje `Directory.Build.props`. Core má implicitné usingy; App používa explicitné usingy. Pri súbehu WPF/WinForms kvalifikuj nejednoznačné typy.
- Súbory používajú file-scoped namespace, anglické názvy typov/metód a príponu `Async` pre asynchrónne operácie. UI texty sú po slovensky; lokalizačný systém zatiaľ nie je.
- Sieťové DTO sú súkromné v `GitLabClient`, JSON API mapuje `SnakeCaseLower`. Lokálny JSON serializuje priamo `List<Account>` s predvoleným nastavením serializéra; pri zmene enumov zachovaj ich uložené číselné hodnoty.
- UI štýly sú v `App.xaml`. `Dashboard` obsahuje prezentáciu a obmedzený prehľad/logy; token drž iba v doménovom účte. Zmeny nastavení vykonávaj cez `MonitorService`, nie priamo zápisom z okna.
- Asynchrónne UI eventy zachytávajú chyby a používajú `FriendlyError`; shutdown cancellation sa nemá hlásiť ako chyba účtu. Nové sieťové operácie musia prenášať cancellation token.
- Testy udržuj pri existujúcom konzolovom kontrolnom programe; v repozitári nie je xUnit/NUnit. `dotnet test` ho nespúšťa. Release workflow konzolové ani desktop kontroly nespúšťa. Nesúvisiace refaktoringy a zmeny závislostí oddeľ od funkčnej úpravy.

Vektorový zdroj ikony je WPF `DrawingImage` v [AppIcon.xaml](../src/GitlabCICDNotify.App/Assets/AppIcon.xaml), ktorý priamo používa dialóg O aplikácii. Aplikácia používa [App.ico](../src/GitlabCICDNotify.App/Assets/App.ico) so samostatnými rámcami 16, 20, 24, 32, 40, 48, 64, 96, 128 a 256 px. Pri úprave ikony zachovaj priehľadné pozadie a aktualizuj XAML aj ICO; skontroluj malé rozmery na svetlom aj tmavom pozadí.

## Rozsah kontrol

| Zmena | Overenie |
| --- | --- |
| Klient API, účet, stav alebo perzistencia | Release build a základný kontrolný program; pridaj relevantnú HTTP/state fixture. |
| WPF layout/binding | Relevantné predchádzajúce kontroly + `--render`, potom otvor a skontroluj príslušné PNG. |
| Tray, HWND alebo menu protokol | `--desktop` v interaktívnej Windows relácii; podľa zásahu aj ručná kontrola shellu. |
| Iba dokumentácia | Faktická kontrola proti kódu, lokálne odkazy a `git diff --check`; build nie je potrebný. |

```powershell
dotnet run --project tests/GitlabCICDNotify.Checks -c Release --no-build -- --render
dotnet run --project tests/GitlabCICDNotify.Checks -c Release --no-build -- --desktop
```

`--render` vytvorí `artifacts/previews/status.png`, `settings.png`, `connect-account.png`, `about.png` a `pipeline.png` so simulovanými údajmi. Pipeline kontroly overujú aj zrušenie rozpracovaného HTTP pri skrytí, pokračovanie bez súbehu po opätovnom otvorení a výber jobu. Program otvorí/zavrie testovacie okná a overí hide/reopen/restore. `--desktop` navyše overuje `GUI_INMENUMODE`, owner HWND, zrušenie menu a callback mimo slučky. Posiela správy iba vlastným testovacím oknám. Render PNG nie je dôkaz správania na rôznych DPI monitoroch.

Testy používajú `FakeGitLab : HttpMessageHandler`, `FakeNotifier : IPipelineNotifier` a dočasný `StateStore(path)`. Nemajú reálny token a neposielajú toasty. DPAPI však vyžaduje skutočný používateľský profil. Výpis `PASS: N checks` ráta aj jednotlivé overenia HTTP hlavičiek, nejde o počet samostatných scenárov. Kontroluj exit code testov osobitne; úspešný nasledujúci publish nesmie zakryť ich pád.

## Skúsenosti z implementačného prostredia

Pri obmedzenom prístupe k používateľským cache pomohli lokálne adresáre:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.tools'
$env:NUGET_PACKAGES = Join-Path $PWD '.tools/packages'
```

Premenné drž konzistentné pri restore/build/run/publish. Restore v sandboxe narazil aj na neprístupný používateľský `NuGet.Config`; samotný `--configfile` prekážku neodstránil a bolo potrebné vykonanie s prístupom k profilu. DPAPI test pod obmedzeným profilom zlyhal na kryptografickej operácii, pod používateľským profilom prešiel. Neobchádzaj to ukladaním tokenov bez šifrovania.

Pri .NET 10 WPF cieli je `ProtectedData` dostupné bez explicitného balíčka; jeho pridaním vzniklo `NU1510` a pri warnings-as-errors zlyhal restore. Native menu test odhalil chybný príznak: `MIIM_FTYPE=0x100`, nie staré `MIIM_TYPE=0x10` kombinované s `MIIM_STRING`. Pack URI ikony obsahuje názov assembly, aby fungovalo aj pri spustení z kontrolného programu.

## Vydanie cez GitHub Actions

Projektový skill [`publish-release`](../.agents/skills/publish-release/SKILL.md) pripraví vydanie na požiadavku, napríklad „vydaj novú verziu“. Ak používateľ neurčí verziu alebo typ zvýšenia, agent ho zvolí podľa obsahu `Unreleased` a SemVer. Prvé vydanie predvolene začína na `0.1.0`; počas úvodného vývoja `0.y.z` sa opravy vydávajú ako patch a nové funkcie či nekompatibilné zmeny ako minor. Stabilné `1.0.0` sa vydá na pokyn používateľa. Skill presunie položky do očíslovanej sekcie, ponechá prázdne `Unreleased`, overí release poznámky, vytvorí commit a anotovaný tag `vX.Y.Z`. Push vykoná iba na výslovný pokyn používateľa.

Workflow `.github/workflows/release.yml` sa spúšťa pri pushi stabilného tagu `vX.Y.Z` (napr. `v1.2.3`). Pred tagovaním presuň vydávané položky z `Unreleased` do samostatnej sekcie `## [1.2.3] - 2026-10-22` v `CHANGELOG.md` a commitni ju. Podporovaný je aj nadpis `## 1.2.3`. Chýbajúca, duplicitná alebo prázdna sekcia zastaví vydanie; `Unreleased` sa nepoužije ako náhrada. Limity MSI pre verziu sú `255.255.65535`; úvodné nuly ani prerelease prípony nie sú podporované.

Po kontrole tagu workflow publikuje self-contained `win-x64` aplikáciu s verziou z tagu, bez trimovania WPF. Projekt nemá unit testy a podľa zvoleného release postupu sa konzolový kontrolný program ani desktop kontroly v CI nespúšťajú. WiX SDK 6.0.2 zabalí celý publish adresár do MSI pre všetkých používateľov v Program Files a pridá odkaz do Štartu. Stabilný UpgradeCode zabezpečuje nahradenie staršej verzie a zákaz downgradu. MSI používa slovenského sprievodcu `WixUI_Minimal` z rozšírenia WiX UI 6.0.2 a vyžaduje správcu. Pred inštaláciou treba potvrdiť licenciu; RTF sa pri zostavení generuje z koreňového `LICENSE` skriptom `build/Convert-LicenseToRtf.ps1` (PowerShell 7). Záverečné okno má predvolene zaškrtnuté spustenie aplikácie, ktoré možno vypnúť. Spustenie je naviazané iba na tlačidlo dokončenia úspešnej interaktívnej inštalácie, nie na tichú inštaláciu, údržbu ani odinštalovanie. Účty v LocalAppData nie sú súčasťou MSI.

Do release sa pripojí `GitlabCICDNotify-X.Y.Z-win-x64.msi` a prenosný `.zip`. Poznámky sú iba z príslušnej sekcie changelogu. Release sa najprv vytvorí ako draft a zverejní až po úspešnom nahratí oboch súborov. Opakovaný beh môže dokončiť existujúci draft; zverejnený release zámerne neprepisuje. Používa vstavaný `GITHUB_TOKEN` s `contents: write`; vlastný PAT nie je potrebný. V nastaveniach repozitára musí byť povolený beh GitHub Actions a zápis release.

Build aj publikovanie prebiehajú v jednom Windows jobe: žiadny `upload-artifact`, zdieľaná build cache ani dočasné Actions artefakty sa neukladajú. Výstupy zostanú iba ako trvalé prílohy release. Pri zlyhaní jobu treba build zopakovať; diagnostika zostáva v štandardnom logu behu.

Lokálne zostavenie rovnakého balíka (PowerShell 7, z koreňa checkoutu):

```powershell
./build/Prepare-Release.ps1 -Tag v1.2.3
dotnet publish src/GitlabCICDNotify.App -c Release -r win-x64 --self-contained true -p:Version=1.2.3 -p:PublishTrimmed=false -p:DebugType=None -p:DebugSymbols=false -o artifacts/publish
dotnet build installer/GitlabCICDNotify.Installer.wixproj -c Release -p:ReleaseVersion=1.2.3 -p:PublishDir="$pwd/artifacts/publish" -o artifacts/installer
```

Pri zmene balenia over self-contained publish a zostavenie MSI. Živú inštaláciu, upgrade a odinštalovanie overuj v čistej Windows relácii; samotný úspešný build ich nepotvrdzuje. Verziu WiX aj schému overuj podľa [dokumentácie WiX](https://docs.firegiant.com/wix/using-wix/); publikovanie používa [GitHub CLI release](https://cli.github.com/manual/gh_release_create).
