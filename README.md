# SebLauncher

Wysuwany pasek (dock) do uruchamiania programów i gier, wywoływany skrótem klawiszowym albo kliknięciem ikony w zasobniku.

## Instalacja
1. Pobierz `SebLauncher.exe` z zakładki [Releases](https://github.com/sebarkh/SebLauncher/releases/latest).
2. Wrzuć go do dowolnego folderu, np. `E:\Programs\SebLauncher`, i uruchom.

Wymagania: Windows 10/11 z .NET Framework 4.8 (wbudowany w system), więc nic nie trzeba doinstalowywać.
Plik `launcher.json` z ustawieniami powstaje obok .exe przy pierwszym uruchomieniu. Przy reinstalacji Windows wystarczy przenieść cały folder.
Jeśli Windows zablokuje pobrany plik: prawy klik → Właściwości → „Odblokuj”.

### Budowanie ze źródeł
`build.cmd` (wymaga .NET SDK 8+) albo `dotnet build SebLauncher.csproj -c Release`.
Każdy tag `v*` buduje i publikuje Release automatycznie przez GitHub Actions.

## Skróty w launcherze

| Skrót | Działanie |
|---|---|
| **Ctrl+Alt+Space** (domyślnie, zmiana w `launcher.json`) | pokaż / schowaj pasek |
| lewy klik na ikonie w zasobniku | pokaż / schowaj pasek |
| pisanie liter | filtrowanie po nazwie i inicjałach (`tc` → Total Commander) |
| **Backspace** | usuń ostatni znak filtra |
| **←** / **→** (także **↑** / **↓**) | poprzednia / następna ikona |
| **Tab** / **Shift+Tab** | następna / poprzednia ikona |
| **Enter** | uruchom zaznaczony program |
| **Ctrl+Shift+Enter** | uruchom jako administrator |
| klik | uruchom |
| **Ctrl+Shift+klik** | uruchom jako administrator |
| **przeciągnij ikonę** w lewo/prawo | zmiana kolejności; przeciągnięcie do innej grupy zmienia kategorię |
| prawy klik na ikonie programu | menu: uruchom jako admin, otwórz lokalizację, zmień nazwę, kategoria, przesuń, usuń |
| **Esc** | czyści filtr; drugi raz chowa pasek |
| pinezka na pasku | pasek przestaje się chować (np. do przeciągania plików) |

Prawy klik na ikonie w zasobniku: skanowanie programów i gier, edycja `launcher.json`, przeładowanie konfiguracji, położenie paska (dół / góra), autostart z Windows, zamknięcie.

## Ustawianie własnego skrótu (`"Hotkey"`)

Format: modyfikatory i jeden klawisz połączone znakiem `+`, wielkość liter nie ma znaczenia.
Przykłady: `"Ctrl+Alt+Space"`, `"Win+Shift+L"`, `"Ctrl+Shift+F1"`, `"Alt+OemTilde"`, `"Ctrl+Alt+NumPad0"`.
Po zmianie: zasobnik → „Przeładuj konfigurację”. Jeśli skrót jest zajęty przez inny program, pojawi się powiadomienie.

### Modyfikatory
| Nazwa | Klawisz |
|---|---|
| `Ctrl` (lub `Control`) | Ctrl |
| `Alt` | Alt (lewy i prawy działają tak samo – nie da się użyć tylko prawego Alt) |
| `Shift` | Shift |
| `Win` (lub `Windows`) | klawisz Windows |

### Nazwy klawiszy
| Nazwa w konfiguracji | Klawisz |
|---|---|
| `A` … `Z` | litery |
| `0` … `9` (albo `D0` … `D9`) | cyfry w górnym rzędzie |
| `NumPad0` … `NumPad9` | cyfry na klawiaturze numerycznej |
| `Add`, `Subtract`, `Multiply`, `Divide`, `Decimal` | `+` `-` `*` `/` `,` na numerycznej |
| `F1` … `F24` | klawisze funkcyjne |
| `Space` | spacja |
| `Enter` (lub `Return`) | Enter |
| `Tab` | Tab |
| `Escape` | Esc |
| `Back` | Backspace |
| `Insert`, `Delete` | Insert, Delete |
| `Home`, `End` | Home, End |
| `PageUp`, `PageDown` | Page Up, Page Down |
| `Left`, `Right`, `Up`, `Down` | strzałki |
| `PrintScreen` | Print Screen |
| `Scroll` | Scroll Lock |
| `Pause` | Pause/Break |
| `CapsLock` | Caps Lock |
| `Apps` | klawisz menu kontekstowego |
| `OemTilde` | `` ` `` / `~` (pod Esc) |
| `OemMinus` | `-` / `_` |
| `OemPlus` | `=` / `+` |
| `OemOpenBrackets` | `[` / `{` |
| `OemCloseBrackets` | `]` / `}` |
| `OemPipe` | `\` / `|` |
| `OemSemicolon` | `;` / `:` |
| `OemQuotes` | `'` / `"` |
| `OemComma` | `,` / `<` |
| `OemPeriod` | `.` / `>` |
| `OemQuestion` | `/` / `?` |
| `VolumeMute`, `VolumeDown`, `VolumeUp` | klawisze głośności |
| `MediaPlayPause`, `MediaNextTrack`, `MediaPreviousTrack`, `MediaStop` | klawisze multimedialne |

Uwagi:
- Skrótów zarezerwowanych przez Windows nie da się przejąć (np. `Win+L`, `Win+D`, `Win+E`, `Ctrl+Alt+Delete`); samo `F12` też jest zarezerwowane przez system.
- Najbezpieczniej wybierać kombinację z co najmniej dwoma modyfikatorami, żeby nie kolidowała ze skrótami w grach i programach.

## Skanowanie
Zasobnik → „Skanuj programy i gry…”. U góry okna przełączniki źródeł (wybór jest zapamiętywany) i wyszukiwarka:
- **E:\Programs** (foldery z `ScanRoots`) – po jednym programie na folder, z listą innych plików .exe do wyboru,
- **C:\Program Files (64 i 32 bit)** – to samo dla `Program Files` i `Program Files (x86)`,
- **Wbudowane w Windows** – Notatnik, Kalkulator, Paint, Terminal, Menedżer zadań, Panel sterowania itd.,
- **Microsoft Store** – aplikacje ze Sklepu innych firm,
- **Pozostałe z menu Start** – programy zainstalowane gdzie indziej (np. w AppData),
- **Gry** – Steam (wszystkie biblioteki), GOG Galaxy, Epic Games, Ubisoft Connect.

Pozycje już dodane są oznaczone „[już dodane]”. „Zaznacz widoczne” zaznacza wszystko, co aktualnie pasuje do filtrów i wyszukiwania.

## launcher.json (obok .exe)
```json
{
  "Hotkey": "Ctrl+Alt+Space",       // np. "Win+Shift+L", "Ctrl+F12"
  "Edge": "Bottom",                  // "Bottom" (nad paskiem zadań) albo "Top"
  "IconSize": 52,                    // 24–128
  "MaxZoom": 1.45,                   // powiększenie pod kursorem, 1.0–2.0
  "HideAfterLaunch": true,
  "ScanRoots": [ "E:\\Programs" ],   // foldery z programami do skanowania (zmień na swoje)
  "Categories": [ "Programy", "Gry" ],
  "ScanSources": [ "Programy", "Gry" ], // źródła pokazywane w oknie skanowania
  "Items": [
    { "Name": "Total Commander", "Path": "E:\\Programs\\totalcmd\\TOTALCMD64.EXE", "Category": "Programy" },
    { "Name": "Cyberpunk 2077", "Path": "steam://rungameid/1091524", "Icon": "C:\\...\\icon.jpg", "Category": "Gry" }
  ]
}
```
Pola elementu: `Name`, `Path` (plik, folder albo URL), `Args`, `WorkDir`, `Icon` (.exe/.ico/.png/.jpg), `Category`, `Admin` (true = zawsze jako admin).
Po ręcznej edycji: zasobnik → „Przeładuj konfigurację”.

Przy reinstalacji Windows wystarczy przenieść cały folder `E:\Programs\SebLauncher` (exe + launcher.json).

## Licencja
MIT – zobacz [LICENSE](LICENSE).
