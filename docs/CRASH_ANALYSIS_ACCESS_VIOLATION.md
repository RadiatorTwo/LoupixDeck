# Crash-Analyse: seltener „Schutzseite"-Fehler im Leerlauf (AccessViolationException)

Status: **Diagnose / native AV noch nicht final eingefangen**; die Robustheits-Härtungen
(Maßnahmen 2, 3, 5, 7) **und** die Render-Pipeline-Serialisierung (Maßnahme 1) sind umgesetzt
— siehe „Behoben: Render-Pipeline serialisiert" und „Behoben: Robustheits-Härtung der
Render-/Serial-Pipeline". Offen bleiben Maßnahme 4 (kontrolliertes Dispose) und 6 (Argus
`Stop()`, Plugin-Repo) sowie die eigentliche AV-Erfassung per Minidump. Ein separater
Cross-Thread-Race im Comms-Layer wurde ebenfalls gefunden und behoben — siehe
„Behoben: Cross-Thread-Race …"
Letzte Aktualisierung: 2026-06-06

## Symptom

- Seltener Absturz der Anwendung **ohne Benutzerinteraktion**, im **Leerlauf**.
- Trat **schon vor** den Interception-Änderungen (Maus-/Tastatur-Macros) auf.
- Fehlermeldung sinngemäß: *„Eine Schutzseite hat einen Fehler"* (genaue Meldung lag nicht vor).

## Einordnung der Meldung

Mit hoher Wahrscheinlichkeit handelt es sich um eine **`AccessViolationException`**. Die
deutsche .NET-Meldung lautet:

> „Es wurde versucht, in **geschützten Speicher** zu lesen oder zu schreiben. Dies ist oft
> ein Hinweis darauf, dass **anderer Speicher beschädigt** ist."

Entscheidend ist der zweite Satz: *„anderer Speicher beschädigt"*. Das ist die typische
Signatur einer **Heap-Korruption durch einen Thread-Race in nativem Code** (hier
**SkiaSharp**). Eine solche Korruption schlägt erst später an zufälliger Stelle zu — daher
**selten, im Leerlauf, ohne erkennbaren Auslöser**.

Wichtig: Eine `AccessViolationException` aus nativem Code lässt sich per `try/catch`
**nicht** abfangen und beendet den gesamten Prozess.

Da der Crash im Leerlauf auftritt, scheiden die eingabegetriebenen Pfade
(Interception/uinput/SendInput) als Ursache praktisch aus. Übrig bleibt der einzige Pfad,
der im Leerlauf von selbst arbeitet: die **timer-gesteuerte Neuzeichnung von Buttons**
(`DynamicTextManager` für Uhr/Sensoren, ggf. ein Plugin-Timer) → SkiaSharp-Rendering +
unsafe-Pixelzugriff.

> **Beobachtung aus dem konkreten Crash-Fall:** Zum Zeitpunkt des Absturzes war als
> einziges Plugin **Argus Monitor** aktiv und hat den Bildschirm aktualisiert; bis zum
> Crash verging **sehr viel Zeit**. Beides passt exakt zum unten beschriebenen Mechanismus
> (langsam akkumulierende Heap-Korruption, getrieben durch häufiges Re-Rendern). Siehe
> Abschnitt „Spezielle Prüfung: Plugin Argus Monitor".

## Grundursache: die Render-/Bitmap-Pipeline ist nicht thread-safe

SkiaSharp-Objekte (`SKBitmap`, `SKCanvas`, `SKPixmap`) sind **nicht** für gleichzeitigen
Zugriff aus mehreren Threads ausgelegt. Im Code gibt es mehrere Stellen, die das
ermöglichen bzw. tun.

### 1. `TouchButton.RenderedImage` wird ohne Synchronisierung gewechselt und gelesen

- `Utils/BitmapHelper.cs:192` — `RenderTouchButtonContent` erzeugt jedes Mal ein **neues**
  `SKBitmap` und setzt `touchButton.RenderedImage = bitmap`.
- `Models/TouchButton.cs:106-114` — der Setter verwirft das alte Bitmap nur, **disposed es
  aber nicht**. Die Freigabe des nativen Pixelpuffers hängt damit am **GC-Finalizer** zu
  nicht-deterministischem Zeitpunkt. Liest parallel jemand dessen Pixel → use-after-free.

### 2. Roher Pixelzeiger ohne Lebensdauer-Garantie

- `LoupedeckDevice/Device/LoupedeckDevice.cs:532-575` — `ConvertSKBitmapToRaw16BppUnsafe`
  holt `srcPtr = pixmap.GetPixels()` und liest in einer langen Schleife, **ohne
  `GC.KeepAlive(bitmap)`** und ohne Null-Check für `PeekPixels()`. Wird das `SKBitmap`
  zwischendurch unreferenziert/finalisiert, zeigt `srcPtr` auf freigegebenen Speicher → AV.
- `Models/Converter/SKBitmapToAvaloniaConverter.cs:31-44` — der UI-Vorschau-Konverter (an
  **jeden** Touch-Button im Hauptfenster gebunden, siehe
  `Views/Devices/LoupedeckLiveSLayout.axaml:205+` und
  `Views/Devices/RazerStreamControllerLayout.axaml:83+`) enthält einen **toten/falschen**
  `fixed` + `UnmanagedMemoryStream`-Block (`stream` wird nie verwendet), liest
  `skBitmap.GetPixels()` ebenfalls ohne `GC.KeepAlive` und kann bei einem leeren/nicht
  peek-baren Bitmap auf eine `NullReferenceException` (`pixmap`) laufen.

### 3. Mehrere Einsprungspunkte rendern off-UI-Thread und mutieren dieselben Bitmaps

`RenderTouchButtonContent` schreibt `RenderedImage` **und** löst `OnPropertyChanged` für die
UI-Bindung aus. Es wird u. a. aus **Threadpool-Threads** aufgerufen:

- `Controllers/LoupedeckLiveSController.cs:630-636` — Exclusive-Modus beendet → Repaint
  (läuft als `async void`-Fortsetzung auf dem Threadpool).
- `Controllers/LoupedeckLiveSController.cs:786-789` — `OnFolderStateChanged` (`async void`).
- `Controllers/LoupedeckLiveSController.cs:95-101` — `RestoreDeviceState`.

Während diese Pfade off-UI rendern, liest der **Avalonia-Render-Thread** parallel dieselben
Buttons über den Konverter. Zwei Threads gleichzeitig in Skia / an `RenderedImage` →
Heap-Korruption.

### 4. Geleakte SkiaSharp-Objekte → Finalizer-Thread gibt nativ frei, während gerendert wird

Dies ist der konkreteste, auch **ohne** einen zweiten App-Thread wirksame Korruptionspfad —
und der, den Argus Monitor zuverlässig befeuert.

- `Utils/BitmapHelper.cs:994-1006` — `DrawTextAt` erzeugt bei **jedem** Render ein
  `SKTypeface` (`SKTypeface.FromFamilyName(...)`) und ein `SKFont`, **ohne `using`/Dispose**.
- `Utils/BitmapHelper.cs:192` — das alte `SKBitmap` aus `RenderTouchButtonContent` wird beim
  Überschreiben von `RenderedImage` nicht disposed (siehe Punkt 1).

Diese SkiaSharp-Wrapper häufen sich an und werden später vom **GC-Finalizer-Thread** nativ
freigegeben — **gleichzeitig** zum aktiven Skia-Rendering auf dem UI-Thread. SkiaSharp-Objekte
sind ausdrücklich **nicht thread-sicher**; native `delete`-Aufrufe des Finalizer-Threads
parallel zur Skia-Nutzung (Font-/Glyph-Cache, PixelRefs) korrumpieren den nativen Heap. Das
ergibt genau die Meldung *„… anderer Speicher beschädigt"* — selten und erst **nach langer
Laufzeit** (es muss erst genug Finalizer-Druck entstehen und ungünstig mit einem Render
zusammenfallen).

### Zusammenfassung des Mechanismus

Unter dem Dauer-„Rauschen" der Idle-Timer (Uhr/Sensoren und/oder ein Plugin-Timer)
überlappen sich diese Pfade gelegentlich, beschädigen den Skia-/Native-Heap, und das fällt
später als `AccessViolationException` auf. Das ist konsistent mit „sehr selten", „ohne
Interaktion" und „hat sehr lange gedauert".

## Spezielle Prüfung: Plugin Argus Monitor

Projekt: `C:\!Code\LoupixDeck.Plugin.Argus` (id `argus`, nur Windows). Es war im konkreten
Crash-Fall das einzige aktive, bildschirm-aktualisierende Plugin.

**Das Plugin selbst korrumpiert den Speicher nicht.** Seine nativen Zugriffe sind sauber
begrenzt:

- `LoupixDeck.Plugin.Argus/ArgusMonitorService.cs:145-214` (`TrySnapshot`) liest aus einem
  Shared-Memory-View via `SafeMemoryMappedViewHandle.AcquirePointer` / `ReleasePointer` in
  `try/finally`. Alle Feldzugriffe gehen über `ReadOnlySpan<byte>.Slice(...)` (bounds-checked
  — ergäbe höchstens eine **fangbare** Exception, keine AV).
- `totalSensorCount` ist auf `MaxSensorCount = 512` gedeckelt
  (`ArgusMonitorService.cs:173-174`); max. Offset ≈ `240 + 512 * 212` ≈ 108 KB ≪ 1 MB View.

**Die entscheidende Rolle von Argus ist die eines Motors für die Render-Pipeline:**

- `LoupixDeck.Plugin.Argus/ArgusSensorCommand.cs:24` — `UpdateInterval => TimeSpan.FromSeconds(2)`.
  `ArgusSensorCommand` ist ein `IDisplayCommand`; der core-`DynamicTextManager` ruft daher
  alle 2 s `GetText` auf und löst pro Tick ein Re-Render eines Touch-Buttons aus
  (`RenderTouchButtonContent` → `DrawTextAt` → unsafe Pixel-Konvertierung).
- Damit entsteht im Leerlauf ein **dauerhafter Strom** an geleakten `SKTypeface`/`SKFont`/
  `SKBitmap`-Objekten (siehe Grundursache Punkt 4). Genau dieser Druck auf den
  Finalizer-Thread ist der plausibelste Auslöser der späten Heap-Korruption.
- `GetText` selbst läuft auf dem Tick-Thread und gibt nur einen `string` zurück; die
  Bildschirmaktualisierung wird auf den UI-Thread marshalled — das Plugin bringt also
  **keine** eigene Cross-Thread-Skia-Nutzung ein, sondern liefert nur die Frequenz.

**Kleinere (nur shutdown-relevante) Anmerkung:** `ArgusMonitorService.Stop()`
(`ArgusMonitorService.cs:72-80`) wartet nur `2 s` auf den Poll-Task und ruft danach
`Close()` (disposed Accessor/MMF/Mutex). Hängt der Poll-Task länger (z. B. in
`_mutex.WaitOne(500 ms)` + `AcquirePointer`), kann der View disposed werden, während der
Task ihn noch nutzt → potenzielle AV **beim Beenden/Deaktivieren** des Plugins (nicht im
Leerlauf). Sauberer wäre, den Task vor dem `Close()` sicher zu Ende laufen zu lassen.

## Plugins / SDK

- Das SDK (`LoupixDeck.PluginSdk`, NuGet-Referenz in `LoupixDeck.csproj`) enthält **kein**
  eigenes `unsafe` / `DllImport` / `Marshal` — dort liegt die Ursache nicht.
- Plugin-Bilder werden in `Services/Plugins/PluginFolderAdapter.cs:65-78` via
  `SKBitmap.Decode(png)` mit `try/catch` dekodiert — robust gegen fehlerhafte PNGs.
- **Relevant für das Problem:** Plugins können `EntriesChanged` aus einem
  **Hintergrund-Thread** feuern (z. B. `Services/Plugins/ExclusiveStressTestProvider.cs:45-52`
  via `System.Threading.Timer`). Über
  `Services/Plugins/ExclusiveModeService.cs:59` (`OnProviderEntriesChanged`) → `StateChanged`
  → `Controllers/LoupedeckLiveSController.cs:543` (`OnExclusiveStateChanged`, `async void`)
  läuft das Rendern dann **off-UI**. Ein Plugin mit Idle-Timer ist damit ein sehr plausibler
  Auslöser des Races — auch ohne Benutzerinteraktion.

## Nebenbefund (geringere Priorität, nicht der Idle-Crash)

- `LoupedeckDevice/SerialDataParser.cs:7,37,64,99` — `_buffer` ist eine unbegrenzt wachsende
  `List<byte>` ohne Obergrenze. Bei dauerhaft fehlerhaften Frames droht
  Wachstum/Out-of-Memory. Eher ein Robustheitsthema als eine AV; sollte trotzdem gedeckelt
  werden.

## Empfohlene Maßnahmen

Priorisiert; 1–3 adressieren den eigentlichen Crash.

1. ✅ **Render-/Konvertierungs-Pipeline serialisieren** — alle Aufrufe von
   `RenderTouchButtonContent` + `ConvertSKBitmapToRaw16BppUnsafe` unter **ein** gemeinsames
   `lock` stellen (oder konsequent auf den UI-Thread marshallen). Schließt das
   Skia-Cross-Thread-Race. *(umgesetzt 2026-06-06 — siehe „Behoben: Render-Pipeline serialisiert")*
2. ✅ **`ConvertSKBitmapToRaw16BppUnsafe` härten** — `GC.KeepAlive(bitmap)` nach der Schleife,
   Null-Check für `PeekPixels()`. *(umgesetzt 2026-06-06)*
3. ✅ **`SKBitmapToAvaloniaConverter` aufräumen** — toten `fixed` / `UnmanagedMemoryStream`-Block
   entfernen, Null-/Empty-Checks ergänzen, `GC.KeepAlive`. *(umgesetzt 2026-06-06)*
4. **Alte `RenderedImage`-Bitmaps kontrolliert disposen** (verzögert, nicht während sie
   gelesen werden) statt sich auf den Finalizer zu verlassen. *(offen)*
5. ✅ **`SKTypeface`/`SKFont` in `DrawTextAt` nicht mehr leaken** (`Utils/BitmapHelper.cs`):
   Typeface zwischenspeichern/wiederverwenden und `SKFont` per `using` entsorgen. Reduziert den
   Finalizer-Druck drastisch und entschärft damit den wahrscheinlichsten Korruptionspfad (Punkt 4),
   der durch Argus' 2-s-Takt befeuert wird. *(umgesetzt 2026-06-06)*
6. (Optional) Poll-Task in `ArgusMonitorService.Stop()` sicher beenden, bevor `Close()` die
   Memory-Mapped-View disposed (verhindert eine AV beim Plugin-Shutdown/-Deaktivieren). *(offen)*
7. ✅ (Optional) `_buffer` im `SerialDataParser` deckeln. *(umgesetzt 2026-06-06)*

Risikoärmste Sofortmaßnahmen: Punkte 2, 3 und **5** (Font-Leak) — sie adressieren den
Idle-Crash direkt und sind klein/isoliert.

## Versuchsergebnis / aktueller Stand (wichtig)

Der Repro-Command (siehe unten) wurde mit maximalem Druck ausgeführt (voller Font-/Bitmap-Leak
+ Dauer-`GC.Collect`/`WaitForPendingFinalizers`) — **kein Crash**. Daraus folgt:

- **Grundursache #4 (Finalizer-Race auf geleakten `SKTypeface`/`SKFont`) ist sehr
  wahrscheinlich NICHT die Ursache.** SkiaSharp 3.116 / Skia hat intern gelockte Font-/Glyph-
  Caches; gleichzeitiges Erzeugen/Finalisieren korrumpiert nicht.
- **Auch „Render-Thread liest `RenderedImage`-Pixel" scheidet aus:** In Avalonia 11.3 **kopiert**
  der `Bitmap(PixelFormat, AlphaFormat, IntPtr, …)`-Konstruktor die Pixel
  (`SKImage.FromBitmap` auf einer nicht-immutablen Bitmap kopiert). Die erzeugte Avalonia-Bitmap
  ist unabhängig vom Quell-`SKBitmap`.
- Mit **nur Argus** aktiv ist der Render-Pfad UI-Thread-serialisiert und ohne geteilten
  Skia-Zugriff über Threads hinweg.

**Konsequenz:** Statt weiter zu theoretisieren, den **echten** Crash instrumentiert einfangen
(Minidump + Managed-Exception-Logging), dann anhand des Fault-Stacks gezielt fixen. Die unsafe-
Härtung (Maßnahmen 2/3) und der Font-Leak (5) bleiben sinnvolle Robustheits-Fixes, sind aber
nach aktuellem Stand vermutlich nicht die Crash-Ursache.

## Behoben: Robustheits-Härtung der Render-/Serial-Pipeline (2026-06-06)

Die vier risikoärmsten Maßnahmen aus „Empfohlene Maßnahmen" (2, 3, 5, 7) sind umgesetzt.
Sie beseitigen die unsafe-/Leak-Pfade als *mögliche* Korruptionsursachen und härten die
Pipeline, auch wenn der instrumentierte Fang der nativen AV (Minidump am echten
Argus-Betrieb) als nächster Schritt offen bleibt.

- **Maßnahme 2 — `ConvertSKBitmapToRaw16BppUnsafe` gehärtet**
  (`LoupedeckDevice/Device/LoupedeckDevice.cs`): Null-/`IsNull`-Check auf das Bitmap,
  `PeekPixels()` per `using` und gegen `null` geprüft, `GetPixels()`-Pointer gegen `null`
  geprüft, `GC.KeepAlive(bitmap)` nach der Schleife. Ein nicht peek-bares Bitmap wirft jetzt
  eine **fangbare** Exception statt einer AV; der Pixelpuffer bleibt während der Konvertierung
  garantiert am Leben.
- **Maßnahme 3 — `SKBitmapToAvaloniaConverter` aufgeräumt**
  (`Models/Converter/SKBitmapToAvaloniaConverter.cs`): toter `fixed` + `UnmanagedMemoryStream`-
  Block entfernt, `PeekPixels()`/`GetPixels()` gegen `null`/`IntPtr.Zero` geprüft (→
  `UnsetValue` statt NRE/AV), `GC.KeepAlive(skBitmap)` über die kopierende
  `Bitmap`-Konstruktion.
- **Maßnahme 5 — `SKTypeface`/`SKFont`-Leak in `DrawTextAt` behoben**
  (`Utils/BitmapHelper.cs`): Typefaces werden in einem statischen
  `ConcurrentDictionary<(Weight,Slant), SKTypeface>` gecacht und wiederverwendet (langlebig,
  bewusst nicht disposed); der per-Größe `SKFont` wird per `using` entsorgt. Reduziert den
  Finalizer-Druck, den Argus' 2-s-Takt erzeugt, drastisch.
- **Maßnahme 7 — Serial-Puffer gedeckelt** (`LoupedeckDevice/SerialDataParser.cs`):
  `_buffer` wird bei Überschreiten von `MaxBufferSize` (4 KiB; legitime Frames sind ≤ 257 Byte)
  verworfen → kein unbegrenztes Wachstum bei dauerhaft fehlerhaftem Stream.

Hinweis: Nach „Versuchsergebnis / aktueller Stand" sind #4 (Finalizer-Race) und
„Render-Thread liest Pixel" als Ursache eher unwahrscheinlich. Diese Fixes sind daher als
**Robustheit** zu werten, nicht als bestätigter Crash-Fix. Maßnahme 1 (Render-Pipeline-
Serialisierung) ist inzwischen umgesetzt (siehe nächster Abschnitt); offen bleiben Maßnahme 4
(kontrolliertes Dispose) und Maßnahme 6 (Argus `Stop()`-Poll-Task, Plugin-Repo), ebenso die
eigentliche AV-Erfassung per Minidump.

## Behoben: Render-Pipeline serialisiert (Maßnahme 1) (2026-06-06)

Alle synchronen SkiaSharp-Draw-/Pixel-Read-Operationen, die off-UI laufen können, werden jetzt
über **ein** gemeinsames Lock serialisiert, sodass nie zwei davon gleichzeitig auf verschiedenen
Threads laufen. Das schließt das im Dokument beschriebene Skia-Cross-Thread-Race konstruktiv aus
(unabhängig davon, ob es der konkrete AV-Auslöser war).

- **Neues gemeinsames Gate**: `Utils/SkiaRenderGate.cs` (`public static readonly object Sync`).
  Bewusst am **Blatt** (den eigentlichen Skia-Operationen) angesetzt statt an jeder Aufrufstelle —
  dadurch sind **alle** Aufrufer (UI-Thread, Device-Send-Pfad, `async void`-Threadpool-
  Fortsetzungen, `DynamicTextManager`-Timer) automatisch serialisiert, ohne jeden Pfad einzeln
  anzufassen.
- **Drei Gate-Punkte** (alle synchron, kein `await`/keine Device-I/O unter dem Lock):
  1. `Utils/BitmapHelper.cs` — `RenderTouchButtonContent`: SKBitmap-Allokation + gesamtes
     Zeichnen (inkl. `DrawLayers`-Helfer) im Lock; nur das `RenderedImage`-Publish + `return`
     bleiben außerhalb (damit UI-marshalled Arbeit aus `OnPropertyChanged` nie unter dem Lock
     läuft).
  2. `LoupedeckDevice/Device/LoupedeckDevice.cs` — `ConvertSKBitmapToRaw16BppUnsafe`: der
     `PeekPixels`/`fixed`/Loop-Block (jeder Device-Framebuffer-Push läuft durch diesen einen
     Chokepoint: `DrawKey`/`DrawScreen`/`DrawCanvas`).
  3. `LoupedeckDevice/Device/LoupedeckDevice.cs` — `DrawTouchSlotsAtomic`: der Composit-Block
     (`canvas.DrawBitmap` der Slot-Bitmaps); das anschließende `await DrawScreen` liegt
     außerhalb des Locks.
- **Bewusst außerhalb des Scopes**: `RenderSimpleButtonImage` (nutzt Avalonia-
  `RenderTargetBitmap`, kein direktes SkiaSharp); die versteckten Exclusive-Modus-Commands
  `System.PlayVideo`/`System.Benchmark` (laufen per Design nicht gleichzeitig mit dem normalen
  Button-Rendering; ihre Device-Pushes nehmen das Convert-Lock ohnehin mit); `SKBitmap.Decode`-
  Stellen (reine Dekodierung, kein geteilter Canvas).
- **Deadlock-Analyse**: Das Lock wird nie über ein `await` oder Device-I/O gehalten; der
  UI-Vorschau-Konverter (`SKBitmapToAvaloniaConverter`) ist **nicht** gegated (er kopiert die
  Pixel ohnehin, siehe „Versuchsergebnis"), sodass kein verschachtelter Lock zwischen UI-Render-
  Thread und Gate entstehen kann. `Monitor` ist zudem pro Thread reentrant.

## Behoben: Cross-Thread-Race auf den Transaktions-Dictionaries (2026-06-04)

**Kein Auslöser der nativen AV, aber ein realer, bestätigter Cross-Thread-Race** — und
ein eigener (managed) Crash-Kandidat. Symptome im Betrieb nach längerer Laufzeit:

> Unexpected error: Operations that change non-concurrent collections must have exclusive access. A concurrent update was performed on this collection and corrupted its state.
> Unexpected error: Index was outside the bounds of the array.

### Ursache

`LoupedeckDevice/Device/LoupedeckDevice.cs` hielt `_pendingTransactions` und
`_pendingTimeouts` als **plain `Dictionary<>`**, die von **zwei Threads** gleichzeitig
mutiert wurden:

- **Send-Queue-Worker** (`Task.Run` in `StartQueueWorker`) — fügt pro Befehl Einträge ein.
- **Serial-Read-Thread** (`SerialConnection.ReadLoop` → `MessageReceived` → `OnReceive`) —
  liest/entfernt Einträge pro Antwort.

`Dictionary<>` ist nicht thread-safe. Gleichzeitiges Insert/Remove beschädigt die internen
**managed** Arrays (`_buckets`/`_entries`): zuerst die `InvalidOperationException`
(„non-concurrent collection …", ausgelöst vom `_version`-Wächter), danach dauerhaft
`IndexOutOfRangeException` („Index was outside the bounds of the array"), weil Lookups/Inserts
über die korrupten Bucket-Grenzen laufen. Beide propagieren über die `TaskCompletionSource`
→ `SendAsync` → `DrawBuffer`/`DrawCanvas`/`DrawKey` in die `Unexpected error:`-Catch-Blöcke.

### Abgrenzung zur nativen AV

Diese Korruption ist **rein managed** und kann den **nativen** Skia-Heap **nicht**
beschädigen — andere Speicher-Domäne, CLR-Bounds-Checking auf managed Arrays liefert nur
**fangbare** Exceptions. Damit ist dieser Race als Ursache der dokumentierten
`AccessViolationException` **ausgeschlossen**.

Er war jedoch ein eigener Crash-Kandidat: Würfe auf dem **Worker-Thread** sind durch dessen
`try/catch` abgesichert (nur geloggt), aber `OnReceive` läuft ungeschützt auf dem
**Read-Thread**. Hätte die Korruption dort zugeschlagen, wäre die Exception unbehandelt
(`AppDomain.UnhandledException`) → Prozess-Ende — geloggt als die Dictionary-Meldung, **nicht**
als AV. Dass es bisher nicht abstürzte, war Timing-Zufall.

### Fix

- Beide Dictionaries → `ConcurrentDictionary<byte, …>` (`using System.Collections.Concurrent;`).
- In `OnReceive` atomares `TryRemove(...)` statt `TryGetValue` + separatem `Remove`.
- `transaction.SetResult` → `TrySetResult`, damit eine bereits per Timeout abgeschlossene
  Transaktion keine `InvalidOperationException` auf dem Read-Thread wirft.

### Bedeutung für die native AV

Bestätigt die Grundthese des Dokuments (es existieren reale, aktive Cross-Thread-Races im
Projekt) und entfernt einen echten Crash-Kandidaten — **identifiziert aber nicht** die
Ursache der nativen AV (anderer Pfad: SkiaSharp). Die AV-Erfassung per `crash.log` / Minidump
am echten Argus-Betrieb bleibt der nächste Schritt.

### Offener Nebenpunkt

Bei **Timeout** werden die Einträge weiterhin nicht aus den Dictionaries entfernt (der
Timeout-Callback setzt nur die TCS-Exception). Mit `ConcurrentDictionary` ist das nun
ungefährlich, die Einträge bleiben aber bis zur Wiederverwendung der Transaction-ID (mod 256)
liegen. Aufräumen im Timeout-Callback wäre sauberer.

## Den echten Crash einfangen (empfohlen)

Zwei komplementäre Mechanismen — der eine fängt **managed** Crashes, der andere **native**:

**1. Managed-Crash-Logger (im Code, immer aktiv).** `Program.InstallCrashLogger()` schreibt
unbehandelte Managed-Exceptions von **jedem** Thread mit vollem Stack nach `crash.log` neben
der Exe (z. B. `bin/Debug/net9.0/crash.log`). Hooks: `AppDomain.UnhandledException`,
`TaskScheduler.UnobservedTaskException`. Fängt z. B. „Collection was modified" oder eine
`NullReferenceException` in einem Timer-/Render-Pfad auf einem Hintergrund-Thread.
- Optional `LOUPIX_FIRSTCHANCE=1` setzen → protokolliert **jede** geworfene Exception (sehr
  laut, aber zeigt die letzte Exception vor einem Absturz).

**2. .NET-Minidump (per Umgebungsvariable, für native AV).** Ein echter nativer Access
Violation (z. B. in Skia) wird vom Runtime *fast-failed* und erreicht den Managed-Logger
NICHT — dafür einen Dump erzeugen lassen:
```
DOTNET_DbgEnableMiniDump=1
DOTNET_DbgMiniDumpType=2          # 2 = Heap
DOTNET_DbgMiniDumpName=…\loupix.%p.dmp
```
Auswerten: `dotnet-dump analyze <dump>` → `clrstack -all` / `threads`.

**Vorgehen:** App mit **echtem Argus** (nicht dem Repro-Command) und gesetzten Dump-Variablen
laufen lassen, bis der Crash kommt. Danach zeigt entweder `crash.log` (managed) oder der Dump
(native) den schuldigen Stack — und erst dann wird gezielt gefixt.

## Reproduktion (Debug-Command) — entfernt

Ein temporärer Debug-Command (`DynamicText.ReproStress`) wurde gebaut, um die Finalizer-Race-
Hypothese (#4) gezielt zu provozieren (voller Font-/Bitmap-Leak + Dauer-GC). Er hat **keinen
Crash** ausgelöst (siehe „Versuchsergebnis / aktueller Stand") und wurde danach wieder
**entfernt**. Nächster Schritt ist die Crash-Erfassung am echten Argus-Betrieb (siehe oben).

## Referenzierte Dateien (Schnellübersicht)

| Bereich | Datei | Zeile(n) |
|---|---|---|
| RenderedImage-Zuweisung | `Utils/BitmapHelper.cs` | 192 |
| RenderedImage-Setter (kein Dispose) | `Models/TouchButton.cs` | 106-114 |
| Unsafe Pixel-Konvertierung (Device-Push) | `LoupedeckDevice/Device/LoupedeckDevice.cs` | 532-575 |
| Unsafe UI-Konverter (tot/ungesichert) | `Models/Converter/SKBitmapToAvaloniaConverter.cs` | 31-44 |
| `SKTypeface`/`SKFont`-Leak je Render | `Utils/BitmapHelper.cs` | 994-1006 |
| Off-UI-Repaint (Exclusive Ende) | `Controllers/LoupedeckLiveSController.cs` | 630-636 |
| Off-UI-Repaint (Folder) | `Controllers/LoupedeckLiveSController.cs` | 786-789 |
| Off-UI-Repaint (Restore) | `Controllers/LoupedeckLiveSController.cs` | 95-101 |
| Exclusive-State → async void | `Controllers/LoupedeckLiveSController.cs` | 543 |
| Plugin-Event off-thread | `Services/Plugins/ExclusiveModeService.cs` | 59 |
| Plugin-Timer (Beispiel) | `Services/Plugins/ExclusiveStressTestProvider.cs` | 45-52 |
| Plugin-Bild-Dekodierung | `Services/Plugins/PluginFolderAdapter.cs` | 65-78 |
| Argus: Shared-Memory-Read (bounded, OK) | `LoupixDeck.Plugin.Argus/ArgusMonitorService.cs` | 145-214 |
| Argus: 2-s-Display-Takt (Render-Motor) | `LoupixDeck.Plugin.Argus/ArgusSensorCommand.cs` | 24 |
| Argus: Stop() disposed View ggf. zu früh | `LoupixDeck.Plugin.Argus/ArgusMonitorService.cs` | 72-80 |
| Unbegrenzter Serial-Puffer | `LoupedeckDevice/SerialDataParser.cs` | 7, 37, 64, 99 |
