# Wolffiles Uploader — Komplett-Plan
_WinUI 3 / .NET 8 · Stand: März 2026_

---

## 1. App-Funktionen (Scope)

| Feature | Status |
|---|---|
| Login (Email + PW) | ✅ Code fertig |
| Upload Queue + Drag & Drop | ✅ Code fertig |
| Fortschrittsbalken + Speed | ✅ Code fertig |
| Upload History (lokal) | ✅ Code fertig |
| Kategorien von API laden | ✅ Code fertig |
| **Registrierung in der App** | 🔧 Geplant |
| **Multilanguage DE/EN/FR** | 🔧 Geplant |
| **Auto-Update System** | 🔧 Geplant |
| **Windows Store (MSIX)** | 🔧 Geplant |
| **GitHub Releases Download** | 🔧 Geplant |
| **Website Download-Button** | 🔧 Geplant |

---

## 2. Registrierung in der App

### Was die App braucht
- Felder: Benutzername, E-Mail, Passwort, Passwort bestätigen
- Optional: Einladungscode (falls Wolffiles das je nutzen will)
- Redirect nach Registrierung → direkt einloggen

### Neue API-Route nötig (Laravel)
```
POST /api/v1/auth/register
Body: { name, email, password, password_confirmation }
Returns: { token, user }
```

### In der App: RegisterPage.xaml
- Link auf LoginPage: "Schon einen Account? Einloggen"
- Gleiches Dark-Design wie LoginPage
- Passwort-Stärke-Anzeige

---

## 3. Multilanguage (DE / EN / FR)

### Ansatz: .resw Dateien (Windows-Standard)
```
Strings/
  de-DE/Resources.resw
  en-US/Resources.resw
  fr-FR/Resources.resw
```

### Sprache speichern
- In Windows `ApplicationData.Current.LocalSettings`
- Sprachauswahl in Settings-Seite
- Sprachumschalter auch in Login/Register sichtbar

### Keys die wir brauchen (Beispiel)
| Key | DE | EN | FR |
|---|---|---|---|
| `Login_Title` | Einloggen | Log in | Se connecter |
| `Queue_Title` | Warteschlange | Upload Queue | File d'attente |
| `Upload_All` | Alle hochladen | Upload all | Tout envoyer |
| `History_Title` | Verlauf | History | Historique |
| `Register_Title` | Registrieren | Register | S'inscrire |
| `Settings_Language` | Sprache | Language | Langue |
| `Logout` | Ausloggen | Log out | Se déconnecter |
| `Drop_Files` | Dateien hierher ziehen | Drop files here | Déposer des fichiers |
| `Error_NoConnection` | Keine Verbindung | No connection | Pas de connexion |
| `Status_Uploading` | Läuft | Uploading | En cours |
| `Status_Done` | Fertig | Done | Terminé |
| `Status_Error` | Fehler | Error | Erreur |
| `Status_Pending` | Wartend | Pending | En attente |

---

## 4. Auto-Update System

### Ansatz: GitHub Releases API + MSIX Installer

#### Wie es funktioniert
1. App startet → prüft `https://api.github.com/repos/wolffileseu/wolffiles-uploader/releases/latest`
2. Vergleicht aktuelle Version (`AssemblyInformationalVersion`) mit `tag_name` vom Release
3. Wenn neue Version → **Benachrichtigung** oben im Fenster (nicht aufdringlich)
4. User klickt "Jetzt aktualisieren" → öffnet GitHub Release URL im Browser
5. Optional (Phase 2): MSIX direkt herunterladen + installieren via `AppInstaller`

#### Empfohlene Methode: .appinstaller Datei
- Eine `wolffiles-uploader.appinstaller` XML-Datei auf wolffiles.eu hosten
- Windows überprüft diese automatisch beim App-Start
- Update-Intervall konfigurierbar (täglich / wöchentlich)
- **Kein Windows Store nötig** für Auto-Updates!

```xml
<!-- wolffiles-uploader.appinstaller -->
<AppInstaller Uri="https://wolffiles.eu/downloads/wolffiles-uploader.appinstaller"
              Version="1.0.0.0">
  <MainPackage Name="WolffilesUploader"
               Version="1.0.0.0"
               Uri="https://wolffiles.eu/downloads/WolffilesUploader.msix"
               ProcessorArchitecture="x64" />
  <UpdateSettings>
    <OnLaunch HoursBetweenUpdateChecks="24" />
  </UpdateSettings>
</AppInstaller>
```

---

## 5. Windows Store (MSIX Packaging)

### Was wir brauchen
- `Package.appxmanifest` — App-Manifest mit Icons, Capabilities
- Zertifikat für Code Signing (kostenlos über Windows Store Developer Account)
- Store Developer Account: einmalig 19 USD

### GitHub Actions CI/CD
```yaml
# .github/workflows/release.yml
- Build MSIX mit dotnet publish
- Signieren mit Zertifikat (GitHub Secret)
- Upload zu GitHub Release
- Copy .msix + .appinstaller zu wolffiles.eu/downloads/
```

### Store-Listing
- Screenshots aus unserem Mockup ✅ (bereits erstellt)
- Kurzbeschreibung DE/EN/FR
- Kategorie: Developer Tools / Utilities

---

## 6. GitHub Release + Website Download-Button

### GitHub
- Repo: `wolffileseu/wolffiles-uploader` (neues public repo)
- Releases mit MSIX + portable ZIP
- Auto-generated via GitHub Actions bei jedem git tag

### Website-Button auf wolffiles.eu
```blade
<!-- Neue Route: /downloads -->
<!-- Zeigt Versionsnummer + Download-Links -->
<a href="https://wolffiles.eu/downloads/wolffiles-uploader.appinstaller">
  ⬇ Windows Installer (.appinstaller)
</a>
<a href="https://github.com/wolffileseu/wolffiles-uploader/releases/latest">
  ⬇ GitHub Release
</a>
```

- Versionsnummer dynamisch von GitHub Releases API laden
- Badge: "v1.2.3 · Windows 10/11"
- Optional: Direktlink für Leute ohne Store

---

## 7. Neue API-Routen (Laravel — wolffiles.eu)

Diese Routen müssen gebaut werden bevor die App fertig ist:

| Route | Methode | Auth | Beschreibung |
|---|---|---|---|
| `/api/v1/auth/login` | POST | Nein | Token holen |
| `/api/v1/auth/logout` | POST | Ja | Token invalidieren |
| `/api/v1/auth/me` | GET | Ja | User-Infos |
| `/api/v1/auth/register` | POST | Nein | Neuer Account |
| `/api/v1/categories` | GET | Nein | Kategorie-Liste |
| `/api/v1/files` | POST | Ja | Datei hochladen |
| `/api/v1/files/my` | GET | Ja | Eigene Uploads |

**→ Nach Implementierung: api-docs.blade.php sofort updaten!**

---

## 8. Implementierungs-Reihenfolge

### Phase 1 — API (Server, zuerst!)
1. `POST /api/v1/auth/login` + `logout` + `me`
2. `POST /api/v1/auth/register`
3. `GET /api/v1/categories`
4. `POST /api/v1/files` (Upload-Endpoint)
5. `GET /api/v1/files/my`
6. → **API Docs aktualisieren**

### Phase 2 — App Kern
1. Multilanguage .resw Dateien erstellen
2. RegisterPage.xaml + ViewModel
3. UpdateCheckerService
4. SettingsPage (Sprache, Account, Version)

### Phase 3 — Distribution
1. Package.appxmanifest + Icons
2. GitHub Actions Release-Workflow
3. .appinstaller auf wolffiles.eu
4. Website Download-Seite `/downloads`
5. Optional: Windows Store Submission

---

## 9. Projektstruktur (final)

```
wolffiles-uploader/
├── WolffilesUploader.csproj
├── App.xaml / App.xaml.cs
├── app.manifest
├── Package.appxmanifest          ← Store/MSIX
├── Assets/                        ← App Icons (PNG verschiedene Größen)
├── Strings/
│   ├── de-DE/Resources.resw
│   ├── en-US/Resources.resw
│   └── fr-FR/Resources.resw
├── Models/
│   └── Models.cs
├── Services/
│   ├── WolffilesApiService.cs
│   ├── AuthService.cs
│   ├── UploadHistoryService.cs
│   └── UpdateCheckerService.cs    ← NEU
├── ViewModels/
│   ├── LoginViewModel.cs
│   ├── RegisterViewModel.cs       ← NEU
│   ├── UploadQueueViewModel.cs
│   ├── HistoryViewModel.cs
│   └── SettingsViewModel.cs       ← NEU
├── Views/
│   ├── MainWindow.xaml
│   ├── LoginPage.xaml
│   ├── RegisterPage.xaml          ← NEU
│   ├── ShellPage.xaml
│   ├── UploadQueuePage.xaml
│   ├── HistoryPage.xaml
│   └── SettingsPage.xaml          ← NEU
├── Converters/
│   └── Converters.cs
└── .github/
    └── workflows/
        └── release.yml            ← NEU
```
