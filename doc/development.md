# Vývoj a overenie

## Prostredie a príkazy

Pracuje sa vo Windows s PowerShellom a .NET 10 SDK. `global.json` požaduje 10.0.100 s `latestFeature`; pri implementácii bol dostupný SDK 10.0.402. Solution má formát `.slnx`. Príkazy spúšťaj z koreňa checkoutu.

```powershell
dotnet restore GitlabCICDNotify.slnx --configfile NuGet.Config
dotnet build GitlabCICDNotify.slnx -c Release --no-restore
dotnet run --project src/GitlabCICDNotify.App -c Release --no-build -- --settings
dotnet run --project tests/GitlabCICDNotify.Checks -c Release --no-build
```

`--status` otvorí stavové okno; bez parametra aplikácia zostane v tray. Publish je framework-dependent adresár vyžadujúci .NET 10 Desktop Runtime, nie samostatný inštalátor:

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
- Testy udržuj pri existujúcom konzolovom kontrolnom programe; v repozitári nie je xUnit/NUnit ani CI konfigurácia. `dotnet test` ho nespúšťa. Nesúvisiace refaktoringy a zmeny závislostí oddeľ od funkčnej úpravy.

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

`--render` vytvorí `artifacts/previews/status.png` a `settings.png` so simulovanými údajmi, otvorí/zavrie testovacie okná a overí hide/reopen/restore. `--desktop` navyše overuje `GUI_INMENUMODE`, owner HWND, zrušenie menu a callback mimo slučky. Posiela správy iba vlastným testovacím oknám. Render PNG nie je dôkaz správania na rôznych DPI monitoroch.

Testy používajú `FakeGitLab : HttpMessageHandler`, `FakeNotifier : IPipelineNotifier` a dočasný `StateStore(path)`. Nemajú reálny token a neposielajú toasty. DPAPI však vyžaduje skutočný používateľský profil. Výpis `PASS: N checks` ráta aj jednotlivé overenia HTTP hlavičiek, nejde o počet samostatných scenárov. Kontroluj exit code testov osobitne; úspešný nasledujúci publish nesmie zakryť ich pád.

## Skúsenosti z implementačného prostredia

Pri obmedzenom prístupe k používateľským cache pomohli lokálne adresáre:

```powershell
$env:DOTNET_CLI_HOME = Join-Path $PWD '.tools'
$env:NUGET_PACKAGES = Join-Path $PWD '.tools/packages'
```

Premenné drž konzistentné pri restore/build/run/publish. Restore v sandboxe narazil aj na neprístupný používateľský `NuGet.Config`; samotný `--configfile` prekážku neodstránil a bolo potrebné vykonanie s prístupom k profilu. DPAPI test pod obmedzeným profilom zlyhal na kryptografickej operácii, pod používateľským profilom prešiel. Neobchádzaj to ukladaním tokenov bez šifrovania.

Pri .NET 10 WPF cieli je `ProtectedData` dostupné bez explicitného balíčka; jeho pridaním vzniklo `NU1510` a pri warnings-as-errors zlyhal restore. Native menu test odhalil chybný príznak: `MIIM_FTYPE=0x100`, nie staré `MIIM_TYPE=0x10` kombinované s `MIIM_STRING`. Pack URI ikony obsahuje názov assembly, aby fungovalo aj pri spustení z kontrolného programu.
