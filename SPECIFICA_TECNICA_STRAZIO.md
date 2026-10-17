# STRAZIO — Specifica tecnica e funzionale

**Applicazione:** `strazio.exe`  
**Versione della specifica:** 1.1 — 17 ottobre 2026  
**Piattaforma:** Windows 10/11, x64 (versione arm64 opzionale)  
**Interfaccia:** riga di comando, completamente in italiano  
**Consegna richiesta:** eseguibile Windows pronto all'uso, senza necessità di installare .NET, un compilatore o Microsoft Excel.

> **Ambito del documento:** questa è la specifica per realizzare l'applicazione, non l'eseguibile già implementato. La sorgente dati secondaria e i dettagli del suo mapping sono estensioni future; la prima versione include un solo adattatore operativo e l'infrastruttura per aggiungerne altri.

## 1. Obiettivo

Realizzare un'applicazione CLI che archivi, in file Excel giornalieri, le estrazioni del **10eLotto ogni 5 minuti**, consentendo:

- scaricamento di una singola giornata o di un intervallo di date;
- scelta e memorizzazione della cartella predefinita;
- prima archiviazione e successivo aggiornamento automatico con **Utilità di pianificazione** di Windows;
- ordinamento crescente o decrescente delle estrazioni;
- messaggi affettuosi in italiano, mostrati in console e come notifiche Windows, attivabili/disattivabili;
- sostituzione e fallback delle fonti tramite architettura **Ports & Adapters**.

Ogni file giornaliero avrà il nome `yyyy-MM-dd.xlsx`, un unico foglio chiamato **`bravo figlio`**, nessuna intestazione e una riga per estrazione effettivamente presente nella fonte.

## 2. Scelte tecnologiche

| Ambito | Scelta | Motivazione |
|---|---|---|
| Linguaggio | **C# 14** | Supporto completo a Windows, CLI, rete, Excel e scheduling. |
| Runtime | **.NET 10 LTS** | Supporto a lungo termine; build autonoma per Windows. |
| Architettura | **Esagonale (Ports & Adapters)** + Application Services | La logica dei concorsi non dipende dall'HTML o dalla libreria Excel. |
| Iniezione dipendenze e logging | `Microsoft.Extensions.Hosting`, `Microsoft.Extensions.DependencyInjection`, `Microsoft.Extensions.Logging` | Gestione uniforme dei servizi e testabilità. |
| Parsing della CLI | `System.CommandLine` | Comandi, opzioni e validazione parametrica; guida personalizzata in italiano con opzione `--guida`. |
| Interazione guidata | `Spectre.Console` | Selezioni numerate, domande comprensibili e progressi in console. |
| Richieste HTTP | `HttpClient` / `IHttpClientFactory` | Timeout, cancellazione e connessioni efficienti. |
| Resilienza | `Polly` | Backoff sui guasti transitori, senza sovraccaricare le fonti. |
| HTML | `AngleSharp` | DOM e selettori CSS per il parser del sito iniziale. |
| Excel | **`ClosedXML`** | Crea XLSX senza Microsoft Excel installato. |
| Notifiche | **Windows App SDK**, `Microsoft.Windows.AppNotifications` | Toast di Windows per applicazioni console non elevate. |
| Pianificazione | **Windows Task Scheduler**, interfaccia COM di Windows | Task giornaliero gestito dal sistema operativo senza processo residente. |
| Configurazione | JSON locale con `System.Text.Json` | Percorso archivio, preferenze e pianificazione. |
| Test | `xUnit` + `FluentAssertions` o asserzioni native, `NSubstitute` facoltativo | Test senza chiamate a Internet, con HTML campione salvato nel progetto di test. |

**Decisione architetturale:** preferire .NET managed in versione 1 rispetto a GraalVM o a una riscrittura in Java. Pubblicare un **single-file self-contained EXE**. Evitare Native AOT in prima battuta perché il supporto completo delle dipendenze Windows App SDK, del parser HTML e di ClosedXML va verificato separatamente.

## 3. Comandi pubblici

Il nome è **sempre** `strazio.exe` (l'esempio `lotto.exe auto-archivia` è considerato un refuso).

| Comando | Descrizione |
|---|---|
| `strazio.exe` | Mostra una breve guida con le principali operazioni. |
| `strazio.exe --guida` | Mostra l'aiuto completo in italiano. |
| `strazio.exe archivia-qui` | Imposta o modifica la cartella predefinita dell'archivio. |
| `strazio.exe scarica` | Procedura guidata: un giorno oppure un intervallo. |
| `strazio.exe scarica --data 2026-10-01` | Scarica un solo giorno senza chiedere le date. |
| `strazio.exe scarica --dal 2026-10-01 --al 2026-10-07` | Scarica un intervallo, estremi compresi. |
| `strazio.exe scarica --data 2026-10-01 --cartella "D:\\Lotto"` | Usa una cartella per questa esecuzione, **senza** modificare quella predefinita. |
| `strazio.exe auto-archivia` | Configura il task automatico e avvia il primo recupero. |
| `strazio.exe ordina-asc` | Salva l'ordinamento crescente e riordina i file esistenti. |
| `strazio.exe ordina-desc` | Salva l'ordinamento decrescente e riordina i file esistenti. |
| `strazio.exe basta-messaggi` | Disabilita subito le frasi, in console e nelle notifiche. |
| `strazio.exe vabbe` | Riabilita le frasi. |
| `strazio.exe stato` | Visualizza percorso, ordinamento, notifiche, ultimo giorno e stato del task. |

**Regole CLI:** `--guida` è l’opzione pubblica di aiuto completo, disponibile anche dopo un sottocomando (ad esempio `strazio.exe scarica --guida`). Il testo della guida deve essere interamente in italiano. Evitare di pubblicare `--help` come opzione di aiuto: se la versione di `System.CommandLine` la registra automaticamente, rimuovere o sostituire quell’alias in configurazione; nomi comandi e opzioni restano italiani dove possibile. Per `scarica`, `--data` non può essere usato insieme a `--dal`/`--al`. Opzioni mancanti in una sessione interattiva vengono richieste mediante domande. Con `--non-interattivo` (uso interno), parametri obbligatori mancanti generano errore anziché attendere input.

### Esempio dell'aiuto (testo effettivamente mostrato in italiano)

```text
STRAZIO — Archivio 10eLotto ogni 5 minuti

Uso:
  strazio.exe <comando> [opzioni]

Comandi:
  archivia-qui      Scegli dove conservare i file Excel.
  scarica           Scarica un giorno o più giorni.
  auto-archivia     Prepara il salvataggio automatico giornaliero.
  ordina-asc        Metti le estrazioni dalla più vecchia alla più recente.
  ordina-desc       Metti le estrazioni dalla più recente alla più vecchia.
  basta-messaggi    Non mostrare più le frasi affettuose.
  vabbe             Mostra di nuovo le frasi affettuose.
  stato             Controlla come è configurato Strazio.

Esempi:
  strazio.exe archivia-qui
  strazio.exe scarica --data 2026-10-01
  strazio.exe scarica --dal 2026-10-01 --al 2026-10-07
  strazio.exe auto-archivia

Per maggiori informazioni: strazio.exe <comando> --guida
```

### Formato delle date

L'input accetta senza distinzione:

1. `01/10/2026` (consigliato all'utente);
2. `1/10/2026`;
3. `2026-10-01` (ideale per script);
4. `oggi` e `ieri` per l'uso interattivo, secondo **Europe/Rome**.

Mostrare sempre un riepilogo normalizzato (`Hai scelto giovedì 1 ottobre 2026. Confermi? [S/n]`). Rifiutare date impossibili e intervalli con fine antecedente all'inizio con un messaggio semplice, senza stack trace.

## 4. Cartelle e impostazioni

### 4.1 Configurazione persistente

Percorso per utente: `%LOCALAPPDATA%\Strazio\settings.json`.

Esempio **di schema**, non di file da distribuire:

```json
{
  "version": 1,
  "archivePath": "D:\\ArchivioLotto",
  "sortOrder": "desc",
  "messagesEnabled": true,
  "scheduledTaskName": "Strazio - Archivio 10eLotto",
  "dailyLocalTime": "10:00",
  "lastSuccessfulSyncUtc": null
}
```

Le impostazioni devono essere salvate con scrittura atomica (file temporaneo + sostituzione). Non salvare le frasi affettuose nel JSON, nei log o in file temporanei dell'applicazione.

**Importante:** le preferenze e i file Excel devono necessariamente rimanere su disco; la richiesta «nessuna traccia fuori dall'EXE» si riferisce al **catalogo delle frasi**, non all'intero stato operativo. Le notifiche Windows possono essere conservate nella cronologia gestita dal sistema operativo: l'app **non può garantire** l'assenza assoluta di copie del testo fuori dall'EXE quando usa i toast.

### 4.2 `archivia-qui`

Mostrare un menu con queste tre voci:

```text
Dove vuoi conservare i file Excel?
  1. Usa questa cartella: C:\Utenti\Mario\Documenti
  2. Crea una cartella chiamata "archivio" qui e usa quella
  3. Scegli un'altra cartella
Scelta [1/2/3]:
```

- La scelta 1 usa la directory di lavoro corrente **al momento dell'avvio**.
- La scelta 2 propone `<directory-corrente>\archivio`; la crea solo dopo conferma.
- La scelta 3 accetta un percorso assoluto, con virgolette facoltative e variabili ambiente note espanse.
- Per ogni scelta verificare: esistenza, percorso valido, directory non file, accesso in lettura/scrittura e spazio sufficiente; verificare i permessi con un file temporaneo poi eliminato.
- Se la cartella manca, dire: `Non trovo questa cartella. Controlla che il nome sia giusto. Vuoi che la crei io? [S/n]`.
- Se l'utente preferisce crearla manualmente, spiegare: `Apri Esplora file (il simbolo della cartella gialla), vai nel punto desiderato, premi il tasto destro, scegli Nuovo > Cartella, scrivi il nome e premi Invio. Poi torna qui.`
- Mostrare il percorso definitivo e chiedere conferma **prima** di modificare il default.
- Al successo: `Perfetto! D'ora in poi conserverò i file qui: ...`.

### 4.3 `scarica`

Senza opzioni, chiedere:

```text
Che cosa vuoi scaricare?
  1. Un solo giorno
  2. Più giorni di seguito
Scelta [1/2]:

Inserisci il giorno (per esempio 01/10/2026):

Dove salvo il file?
  [Invio] Usa la cartella predefinita: D:\ArchivioLotto
  Oppure scrivi un'altra cartella:
```

Se manca la cartella predefinita, spiegare che è necessario sceglierne una, ma **non aggiornarla** durante `scarica`. Accettare e normalizzare date/formati. Chiedere conferma prima di richieste con più di 31 giorni. Non sovrascrivere un Excel esistente e valido, salvo consenso esplicito o `--forza`.

## 5. `auto-archivia`: procedura dettagliata

### 5.1 Controllo dei privilegi

La prima verifica funzionale è capire se il processo è elevato come amministratore (token Windows), **prima di modificare task o impostazioni di scheduling**.

Se non lo è, uscire senza creare alcun task e mostrare:

```text
Per preparare il salvataggio automatico serve aprire Strazio
come amministratore. Non hai fatto niente di sbagliato.

1. Premi il pulsante Start di Windows.
2. Scrivi "Terminale" oppure "Prompt dei comandi".
3. Premi il tasto destro sul programma trovato.
4. Scegli "Esegui come amministratore".
5. Quando Windows chiede il permesso, premi "Sì".
6. Nella nuova finestra copia questo comando e premi Invio:

   "C:\Strazio\strazio.exe" auto-archivia

Se non sai dove si trova Strazio, apri la cartella che
contiene strazio.exe e copia il suo percorso dalla barra in alto.
```

Nel testo reale sostituire `C:\Strazio\strazio.exe` con **il percorso assoluto effettivo** del processo corrente, opportunamente racchiuso fra virgolette. Se si usa PowerShell, anteporre `&` al percorso fra virgolette: `& "C:\Strazio\strazio.exe" auto-archivia`.

Se Windows richiede credenziali di **un altro account** amministratore, non trasferire automaticamente la configurazione di quell'altro profilo all'utente originale: avvisare che profili differenti hanno impostazioni differenti.

### 5.2 Raccolta dati e inizializzazione

Una volta confermati i privilegi:

1. Mostrare la cartella archivio già configurata, se presente, come valore predefinito. L'utente può confermare con **Invio** o digitare un nuovo percorso.
2. Validare la cartella come descritto in §4.2. Se manca, spiegare come crearla e offrirsi di crearla dopo conferma.
3. Cercare nella cartella solo i file con nome rigorosamente `^\d{4}-\d{2}-\d{2}\.xlsx$`, validando le date e la struttura dei file candidati.
4. Individuare il file giornaliero **valido** più recente; rilevare anche eventuali giorni mancanti nel periodo già archiviato e proporre di recuperarli.
5. Proporre come data iniziale **il giorno successivo all'ultimo file valido**. Se non esiste alcun file, chiedere la prima data senza supporre che l'utente voglia scaricare tutti i dati dal 2018.
6. Chiedere l'orario giornaliero: `A che ora vuoi controllare le nuove estrazioni? [10:00]`. L'orario indicato è **l'ora locale del computer**. Il limite delle giornate scaricabili viene invece calcolato sulla data italiana.
7. Confermare percorso, data di partenza, frequenza, eventuali vuoti e orario.
8. Registrare/aggiornare il task idempotentemente e **avviare subito** il recupero iniziale (senza chiedere un altro comando).
9. Mostrare quantità di giorni archiviati, già presenti, non disponibili ed errori.

### 5.3 Task di Windows

**Nome task:** `Strazio - Archivio 10eLotto`.

- Trigger: ogni giorno alle 10:00 ora locale come valore iniziale (modificabile nel wizard).
- Trigger aggiuntivo: all'accesso dell'utente, con recupero dei giorni non ancora archiviati; evitare esecuzioni sovrapposte.
- Azione: percorso **assoluto** dell'EXE + comando interno `__esegui-pianificato --non-interattivo`.
- Directory di lavoro: directory dell'EXE o directory esplicitamente configurata, mai presupporre `C:\Windows\System32`.
- Usare l'account Windows del proprietario dell'archivio; prevedere modalità non interattiva per il funzionamento senza sessione desktop, quando le credenziali e le policy lo consentono. Se non è configurabile, usare la modalità «solo quando l'utente ha effettuato l'accesso» e avvisare della limitazione.
- Configurare «Esegui il prima possibile se l'avvio pianificato viene perso», retry su errore transitorio e divieto di esecuzioni parallele.
- Non eseguire il recupero quotidiano come `SYSTEM` per impostazione predefinita.
- Non richiedere privilegi amministrativi **per il recupero giornaliero**: sono richiesti solo per la configurazione iniziale secondo il requisito dell'app.
- Il percorso dell'EXE usato dal task deve essere stabile: prima di creare il task avvisare `Se sposti strazio.exe da questa cartella, il salvataggio automatico non funzionerà finché non lo riconfiguri`.

**Criterio temporale:** il task scarica soltanto date **precedenti alla data odierna nel fuso `Europe/Rome`**, così non registra come definitivo un giorno ancora in corso; le richieste manuali possono includere oggi, ma il file viene considerato provvisorio e potrà essere aggiornato successivamente.

**Fallimento parziale:** un giorno fallito non blocca il salvataggio dei giorni successivi; resta segnalato per il recupero futuro. Le esecuzioni successive colmano eventuali buchi invece di avanzare ciecamente dall'ultimo filename.

## 6. Frasi affettuose e notifiche

### 6.1 Catalogo incorporato

Il secondo artefatto del progetto è **`frasi_affettuose_strazio.txt`**, UTF-8, una frase per riga. È un **asset di compilazione** incorporato come *embedded resource* nell'assembly (manifest resource); **non deve essere distribuito** insieme all'EXE o estratto intenzionalmente in una cartella. Non includere il catalogo nelle informazioni di diagnostica, nei log o nel file delle preferenze.

Le frasi contengono italiano colloquiale, qualche forma di romanesco e toni teneri ma rispettosi. Ogni nuova interazione esplicita con la CLI sceglie **una** frase usando `RandomNumberGenerator.GetInt32(n)` per ottenere una distribuzione uniforme.

### 6.2 Comportamento

- Preferenza iniziale: `messagesEnabled = true`.
- Prima del normale output di ogni comando interattivo (compresi `stato`, errori di sintassi e richieste d'aiuto), selezionare una frase casuale e mostrarla **una volta** in console.
- Inoltre tentare di mostrarla come **notifica Windows** nello stesso avvio, se il sistema supporta i toast, l'utente è collegato e il processo **non** è elevato.
- Per le fasi successive dello stesso wizard **non ripetere** la frase dopo ogni domanda: una frase per invocazione è sufficiente.
- `basta-messaggi` è **eccezione necessaria**: disabilitare e salvare la preferenza **prima di qualunque presentazione**, senza mostrare altre frasi. Risposta: `Va bene, da adesso niente più frasi. Se cambi idea, scrivi: strazio.exe vabbe`.
- `vabbe` riabilita, salva subito e mostra immediatamente una frase anche nella notifica quando possibile: `E daje, rieccoci!` seguito dalla frase scelta.
- Le invocazioni pianificate con `--non-interattivo` **non** mostrano frasi o toast affettuosi: non sono interazioni iniziate dall'utente e non devono generare notifiche quotidiane indesiderate.
- Se i toast non sono disponibili, le operazioni continuano: l'errore dell'interfaccia di notifica non può far fallire lo scaricamento dei dati.

**Vincolo tecnico verificato:** Windows App SDK non supporta notifiche emesse direttamente da un processo eseguito **come amministratore**. Pertanto `auto-archivia` elevato può mostrare la frase in console ma **non può garantire il toast in quel processo**. Una futura variante potrebbe affidare il toast a un processo Strazio *non elevato* avviato nella sessione dell'utente; la versione iniziale deve documentare e rispettare il limite anziché promettere notifiche impossibili.

**Privacy e sistema operativo:** se un toast appare, Windows potrebbe conservarlo nel Centro notifiche. Il catalogo resta incorporato nell'EXE, ma non è tecnicamente possibile garantire la richiesta letterale di «zero tracce» nel sistema operativo.

## 7. Formato esatto del file Excel

### 7.1 Nome e foglio

- Nome: `yyyy-MM-dd.xlsx`, per esempio `2026-10-01.xlsx`.
- Unico worksheet: **`bravo figlio`**, senza altri fogli nascosti.
- **Nessuna riga d'intestazione**, nessun commento, nessun campo aggiuntivo fuori dallo schema.
- Prima estrazione nella riga **1** (non nella 2).
- Una riga per ciascuna estrazione trovata e convalidata: normalmente **288**, ma se la pagina ne contiene meno, **non inventare** righe mancanti.

### 7.2 Colonne

| Colonna Excel | Tipo | Contenuto |
|---|---|---|
| `A` | Data Excel con formato `dd/MM/yyyy` | Data dell'estrazione. |
| `B` | Numero intero | Numero progressivo del concorso. |
| `C:T` | 18 numeri interi | I **18** numeri estratti ordinari, escludendo sia Oro sia Doppio Oro dai 20 numeri della fonte. |
| `U` | Numero intero, **grassetto blu** | **Oro** (un solo numero). |
| `V` | Numero intero, **grassetto blu** | **Doppio Oro**: solo il **secondo numero speciale**, senza ripetere Oro. |
| `W:X` | Celle vuote | Lasciare realmente vuote. |
| `Y` | Data Excel con formato `dd/MM/yyyy` | Stessa data di `A`, scritta come valore autonomo. |
| `Z` | Numero intero | Stesso numero di `B`, scritto come **valore autonomo**, non come formula o riferimento. |
| `AA:AO` | 15 numeri interi | I **15** numeri Extra. |

**Correzione della richiesta originale:** `AA:A8` non è un intervallo valido di 15 colonne: **`A8` è una cella**, mentre le 15 colonne da `AA` terminano in **`AO`**.

**Formattazione obbligatoria dei numeri Oro:** nelle colonne **U e V**, tutte le celle popolate (a partire dalla riga 1) devono avere il **carattere blu, in grassetto**. Usare il colore RGB **`#0000FF`** (blu puro) e una formattazione numerica intera, senza decimali. Il colore si applica **al testo/numero della cella**, non allo sfondo. Non applicare automaticamente lo stesso stile alle altre colonne. Il formato deve sopravvivere a creazione, aggiornamento e riordinamento del file.

**Regole del mapping**:

1. Il parser acquisisce **20** numeri ordinati come pubblicati nella fonte, il valore `oro`, il valore `doppioOroSecondoNumero` e la lista Extra.
2. Verificare che Oro e Doppio Oro siano **due numeri distinti** presenti fra i 20 numeri; in caso contrario segnalare dati anomali, non eliminare numeri a caso.
3. Filtrare ciascuno dei due numeri speciali **una sola volta** dalla lista dei 20; devono restarne esattamente **18**. Conservare l'ordine dei rimanenti, senza ordinare i singoli numeri in base alla preferenza `ordina-*`.
4. Per `AA:AO`, conservare l'ordine Extra della fonte.
5. Prima della disponibilità storica di Extra (la fonte dichiara **10 settembre 2020**), scrivere **15 celle vuote** e registrare internamente il dato come «Extra non disponibile», non come estrazione incompleta o serie di zeri.
6. Non aggiungere tabelle Excel, formule, filtri, altri fogli o intestazioni. Stile minimale: date leggibili, interi senza decimali, larghezza colonne adeguata e **U/V obbligatoriamente in grassetto blu (`#0000FF`)**; qualsiasi formattazione facoltativa non deve modificare valori o struttura.

### 7.3 Ordinamento

Ordinamento iniziale **decrescente**: concorso maggiore nella prima riga (l'ultimo concorso della giornata prima degli altri). `ordina-asc` ordina per numero di concorso crescente. **La preferenza è persistente** per tutte le esportazioni successive.

Per rispettare il significato dei comandi senza parametri, `ordina-asc` e `ordina-desc` devono inoltre **riordinare i file XLSX giornalieri già esistenti** nella cartella archivio predefinita, previa conferma del numero di file coinvolti. Devono modificare solo l'ordine delle righe, mantenendo invariati valori e formato, con sostituzione atomica e salvataggio preventivo. Se non c'è ancora alcun file, cambia soltanto la preferenza. In caso di file aperto/bloccato da Excel, segnalarlo e proseguire sugli altri, senza corrompere il file.

### 7.4 Esempio verificabile dal sito

Per il **concorso 1 del 1 ottobre 2026**, la fonte mostra:

- 20 numeri: `10 12 20 25 27 30 31 39 42 43 46 51 53 66 73 74 75 76 83 85`;
- Oro `30`; Doppio Oro (secondo numero) `25`;
- Extra: `2 4 11 24 28 40 44 55 59 65 70 71 77 79 88`.

Quindi `C:T` contiene esattamente: `10 12 20 27 31 39 42 43 46 51 53 66 73 74 75 76 83 85`; `U=30`, `V=25`, `Y=01/10/2026`, `Z=1`, `AA:AO=2 4 11 24 28 40 44 55 59 65 70 71 77 79 88`. La riga fisica di Excel dipende dall'ordinamento: con `desc` il concorso 1 compare in fondo al foglio.

## 8. Architettura: porte e adattatori

```text
┌───────────────────────────────────────────────────┐
│ STRAZIO CLI (System.CommandLine + Spectre.Console) │
│ • Guida • Domande • Messaggi • Toast • Scheduling  │
└───────────────────────┬───────────────────────────┘
                        │
                 Application Services
         ArchiveSyncService / ArchiveSettingsService
                        │
         ┌──────────────┼──────────────────┐
         │              │                  │
  IExtractionSource  IArchiveWriter  ISettingsRepository
         │              │                  │
  ExtractionSource   ClosedXmlWriter   JsonSettingsStore
  Provider / Router
         │
    ┌────┴──────────┐
    │               │
 10elotto5.it   Fonte futura (placeholder)
 HTML adapter    adapter n
    │
 HttpFetcher → HtmlExtractor → ExtractionMapper
```

### 8.1 Modello di dominio (immutabile)

```csharp
public sealed record Draw(
    DateOnly Date,
    int CompetitionNumber,
    IReadOnlyList<int> MainNumbers20,
    int Oro,
    int DoppioOroSecondNumber,
    IReadOnlyList<int>? ExtraNumbers15);

public sealed record DrawDay(
    DateOnly Date,
    IReadOnlyList<Draw> Draws,
    string SourceId,
    bool IsProvisional);
```

Al writer passare un modello già normalizzato `ArchiveRow` con `MainNumbers18`. Le collezioni devono essere validate prima della scrittura. Non introdurre dipendenze HTML o ClosedXML nei progetti Domain/Application.

### 8.2 Porte

```csharp
public interface IExtractionSource
{
    string Id { get; }
    Task<DrawDay> FetchAsync(DateOnly date, CancellationToken ct);
}

public interface IExtractionSourceProvider
{
    IReadOnlyList<IExtractionSource> GetPriorityOrderedSources();
}

public interface IArchiveWriter
{
    Task WriteAsync(DrawDay day, string destination, SortOrder order,
                    CancellationToken ct);
}

public interface ISettingsRepository
{
    Task<AppSettings> LoadAsync(CancellationToken ct);
    Task SaveAsync(AppSettings settings, CancellationToken ct);
}

public interface IUserMessages
{
    string PickRandom();
    Task PresentAsync(string message, bool showToast, CancellationToken ct);
}

public interface IScheduledArchiveService
{
    Task CreateOrUpdateAsync(ScheduleDefinition schedule, CancellationToken ct);
    Task<ScheduleStatus> GetStatusAsync(CancellationToken ct);
}
```

Separare all'interno dell'adattatore `IPageFetcher` (HTTP), `IRawExtractionParser` (HTML → struttura grezza specifica del sito), `IDrawMapper` (struttura grezza → dominio). Il provider/routing non deve conoscere selettori HTML; mantiene priorità e stato delle fonti.

### 8.3 Adattatore iniziale

- **ID:** `10elotto5.it-html`
- **URL parametrizzato:** `https://www.10elotto5.it/archivio-10elotto5-minuti/?data={yyyy-MM-dd}`.
- Esempio: `https://www.10elotto5.it/archivio-10elotto5-minuti/?data=2026-10-01`.
- `HttpFetcher`: GET con timeout, `Accept: text/html`, user-agent identificabile, senza eseguire JavaScript e senza scaricare pubblicità/risorse aggiuntive.
- `RawExtractionParser`: cercare nella risposta un elemento `h2` il cui testo contenga `Estrazioni 10eLotto 5 minuti` e la data richiesta in `dd/MM/yyyy`; trovare il tavolo **`table.tabella-desktop`** e leggerne i concorsi e la rispettiva riga **EXTRA**. Ignorare la rappresentazione mobile duplicata e qualsiasi tabella pubblicitaria.
- Preferire selettori semantici e riconoscimento del numero di concorso; non basarsi su indici assoluti fragili della pagina. Salvare nel progetto di test un fixture HTML autorizzato del 1/10/2026.
- `DrawMapper`: ricostruire ogni concorso con numero, data, 20 numeri, Oro, Doppio Oro e 15 Extra; verificare associazione corretta della riga EXTRA al concorso.

**Validazione minima dell'HTML:** HTTP `200`, `h2` corretto, data coincidente con la richiesta, `table.tabella-desktop` presente, almeno **un** concorso leggibile e nessuna struttura dati incongruente. **Non** imporre `>100 <tr>` nell'app: quella soglia era utile per il precedente script di stress test, ma qui è sbagliata perché esistono giorni parziali.

### 8.4 Validazioni dei dati

- Numeri nel range `1..90`.
- Esattamente 20 numeri estratti **unici** per ogni concorso.
- Due numeri speciali distinti e inclusi nei 20.
- Per le date dall'avvio storico di Extra: esattamente 15 Extra **unici**, salvo eccezione esplicita e documentata dalla fonte.
- Numero concorso positivo e non superiore a 288 per la fonte attuale; vietati numeri duplicati nello stesso giorno.
- **Non** assumere che siano sempre presenti 288 concorsi o numerazioni perfettamente continue: verificare e segnalare gli eventuali salti, senza crearli artificialmente.
- Se il titolo `h2` indica `(N concorsi)`, confrontare `N` con il numero dei concorsi distinti estratti dal DOM; non scrivere un Excel parziale in caso di discrepanza non spiegata.
- Non usare i numeri presenti nell'HTML mobile, nella pubblicità o in altri widget come dati aggiuntivi.
- La data di archivio proviene dalla richiesta e deve essere confermata dal titolo HTML; rifiutare risposte che mostrano un giorno diverso.

### 8.5 Fallback fra sorgenti

`IExtractionSourceProvider` fornisce le fonti nell'ordine di priorità impostato. Per **un giorno**:

1. Tentare la prima fonte disponibile.
2. Se c'è timeout, errore di rete transitorio, HTTP 5xx o schema HTML incompatibile, registrare l'errore tecnico e passare alla fonte successiva **se realmente implementata e validata**.
3. Se tutte le fonti falliscono, riportare un fallimento per quel giorno, continuando sugli altri giorni dell'intervallo.
4. **Non** usare il fallback per aggirare un blocco, divieto d'accesso, HTTP `403` o un limite `429`: rispettare `Retry-After` e interrompere o sospendere l'operazione secondo le indicazioni del servizio. Non usare proxy rotanti o user-agent falsificati.
5. Nessuna fonte alternativa deve essere inventata nella configurazione: l'implementazione iniziale ne contiene una sola; il «fallback» diventa operativo quando è implementato almeno un secondo adattatore.

### 8.6 Ritmi di accesso e recupero errori

- Una sola richiesta HTTP alla volta per i download archivio (`maxConcurrency = 1`).
- Pausa casuale, ad esempio **3–8 secondi fra richieste**, con jitter e possibilità di configurazione interna; non tentare di nascondere l'identità del client.
- Timeout predefinito per richiesta: **30 secondi**; retry limitati (massimo 2 tentativi per fonte su guasti transitori), con backoff crescente e jitter.
- Per `429`, rispettare `Retry-After`; in assenza di un valore utile, sospendere la fonte e fermare il batch senza effettuare fallback aggirante.
- Verificare e rispettare robots.txt, condizioni d'uso, eventuali limitazioni sulla riutilizzazione dei dati e preferire una fonte ufficiale/licenziata o un'API autorizzata se disponibile.
- Se un batch è interrotto, i giorni già validamente salvati rimangono intatti e il prossimo tentativo riparte dai giorni assenti.

## 9. Scrittura sicura e idempotenza

1. Per ogni giorno, creare il workbook **in memoria**.
2. Convalidare schema, righe, date e ordinamento.
3. Salvare temporaneamente un `.xlsx` in una directory di lavoro privata e verificare aprendolo nuovamente con ClosedXML.
4. Sostituire il file di destinazione con operazione il più possibile atomica sullo stesso volume (e rollback/backup in caso di sostituzione).
5. Non lasciare un file incompleto chiamato `yyyy-MM-dd.xlsx`.
6. Se il file è già presente e valido, saltarlo con messaggio `Già presente`; in caso di file corrotto, chiedere prima di sostituire nell'uso interattivo e applicare una politica documentata e prudente nel batch.
7. I file del giorno corrente sono **provvisori** e riscaricabili nelle sincronizzazioni seguenti; i giorni conclusi non vengono sovrascritti automaticamente senza ragione, salvo `--forza`.
8. Serializzare gli accessi con un mutex di processo per cartella archivio, prevenendo l'esecuzione contemporanea del task e di `scarica` / `ordina-*`.

Le scritture atomiche e i test devono coprire interruzione del processo, mancanza di spazio, cartella non scrivibile e file aperto in Excel.

## 10. Esempi di messaggi in italiano

**Successo:**

```text
Ho trovato 288 estrazioni per il 01/10/2026.
Sto preparando il file Excel...
Fatto! Ho salvato:
D:\ArchivioLotto\2026-10-01.xlsx
```

**Giorno con meno estrazioni:**

```text
Per il 17/10/2026 ho trovato 96 concorsi.
Salvo soltanto quelli disponibili: non aggiungo righe vuote o inventate.
```

**Errore nella data:**

```text
Questa data non sembra giusta: 31/02/2026.
Per favore prova con il formato giorno/mese/anno,
per esempio 01/10/2026.
```

**Cartella non accessibile:**

```text
Non riesco a scrivere nella cartella scelta.
Controlla che esista e che non sia protetta.
Vuoi sceglierne un'altra? [S/n]
```

**Sito temporaneamente indisponibile:**

```text
Oggi il sito non risponde correttamente.
Non ho modificato i file già salvati.
Puoi riprovare più tardi: strazio.exe scarica
```

**Messaggi disattivati:**

```text
Va bene, da adesso niente più frasi affettuose.
Se cambi idea, scrivi: strazio.exe vabbe
```

## 11. Struttura del repository

```text
src/
  Strazio.Cli/                   # Eseguibile e comandi
  Strazio.Application/           # Casi d'uso e porte
  Strazio.Domain/                # Draw, DrawDay, validazione, ArchiveRow
  Strazio.Infrastructure.Http/   # HTTP, parser, mapper, fonte HTML
  Strazio.Infrastructure.Excel/  # ClosedXmlWriter
  Strazio.Infrastructure.Win/    # Task Scheduler e toast Windows
  Strazio.Infrastructure.Config/ # JSON locale e risorsa embedded
resources/
  frasi_affettuose_strazio.txt  # EmbeddedResource, esclusa dalla distribuzione

tests/
  Strazio.Domain.Tests/
  Strazio.Application.Tests/
  Strazio.Integration.Tests/
  Fixtures/2026-10-01.html
```

Se si desidera ridurre il numero di progetti, `Infrastructure.*` può essere raggruppato in un solo progetto **senza** perdere la separazione delle interfacce.

## 12. Distribuzione Windows

**Output finale richiesto:** `strazio.exe`, distribuibile con copia su un computer Windows x64 supportato e avviabile senza compilazione o installazione preventiva di .NET/Excel.

Indicazione di pubblicazione (da verificare con tutte le dipendenze Windows SDK):

```powershell
dotnet publish .\src\Strazio.Cli\Strazio.Cli.csproj `
  -c Release -r win-x64 --self-contained true `
  -p:PublishSingleFile=true `
  -p:EnableCompressionInSingleFile=true `
  -p:AssemblyName=strazio
```

- Il binario **non è automaticamente compatibile** con ogni architettura: produrre un'ulteriore build `win-arm64` se richiesta.
- Il singolo EXE può estrarre componenti runtime nella cartella temporanea di Windows. La prova di rilascio deve dimostrare che l'utente riceve **un solo eseguibile** e non deve installare dipendenze manuali.
- Se Windows App SDK richiede impostazioni aggiuntive per una build single-file effettivamente autonoma, configurare il self-contained SDK o valutare un componente di notifiche nativo che non richieda runtime esterni. Questo è uno **spike tecnico bloccante** prima della consegna, non un dettaglio da dare per scontato.
- Il programma deve mostrare un avviso ragionevole su eventuali restrizioni Windows SmartScreen per eseguibili non firmati; preferire firma Authenticode per distribuzione a terzi.
- Le frasi devono essere verificate come risorse embedded nel binario; nessun `.txt` o `.json` di frasi deve essere presente nella cartella di distribuzione.
- La configurazione di scheduling e delle notifiche può produrre normali registrazioni di Windows (task, COM/identità notifiche); non promuovere una promessa di «nessuna traccia fuori dall'EXE» per queste registrazioni di sistema.

## 13. Criteri di accettazione

| ID | Prova | Risultato atteso |
|---|---|---|
| AC-01 | Aprire `strazio.exe --guida` su un Windows pulito | Guida leggibile, soltanto in italiano; nessuna installazione di .NET/Excel. |
| AC-02 | Avviare un comando interattivo con messaggi abilitati | Una frase in console e un toast quando supportato (account non elevato). |
| AC-03 | `basta-messaggi`, poi `stato` | Nessuna frase, nessun toast affettuoso; stato persistente dopo il riavvio. |
| AC-04 | `vabbe`, poi `stato` | Messaggi nuovamente abilitati e visualizzati. |
| AC-05 | `archivia-qui` con 1/2/3 | Cartella esistente o creata con permesso, conferma e persistenza. |
| AC-06 | `scarica` e scelta di cartella diversa | File nella nuova cartella, predefinita invariata. |
| AC-07 | `scarica --data 2026-10-01` con fonte campione | `2026-10-01.xlsx`, unico foglio `bravo figlio`, 288 righe, senza intestazioni. |
| AC-08 | Aprire file AC-07 | Colonne `A`, `B`, `C:T`, `U`, `V`, `W:X`, `Y`, `Z`, `AA:AO` esattamente come da §7; ogni cella non vuota in `U` e `V` ha carattere **grassetto blu `#0000FF`**. |
| AC-09 | Ispezionare concorso 1 del 01/10/2026 | I 18 numeri di §7.4, Oro 30, Doppio Oro 25, 15 Extra corretti. |
| AC-10 | `ordina-desc` | Prima riga = concorso più alto disponibile; altri file preesistenti riordinati senza perdita. |
| AC-11 | `ordina-asc` | Prima riga = concorso più basso disponibile; impostazione persistente. |
| AC-12 | Giorno con 96 concorsi | Esattamente 96 righe, non 288 artificiali. |
| AC-13 | `auto-archivia` senza privilegi elevati | Istruzioni semplici per riaprire come amministratore; nessun task creato. |
| AC-14 | `auto-archivia` con privilegi elevati | Cartella/data/orario confermati, task presente, primo download eseguito. |
| AC-15 | Task eseguito due volte | Nessun duplicato o file corrotto; eventuali giorni mancanti recuperati. |
| AC-16 | HTTP 500 o struttura HTML mutata | Errore diagnosticato, fonte successiva se configurata, nessun Excel errato. |
| AC-17 | HTTP 429 | Rispetto del blocco/rate-limit; nessuna strategia di aggiramento. |
| AC-18 | Aprire lo stesso XLSX mentre Strazio tenta di cambiarlo | Errore gestito; file originale intatto. |
| AC-19 | Distribuire solo `strazio.exe` | Nessun `frasi_affettuose_strazio.txt` sul computer di destinazione. |
| AC-20 | Toast con app elevata | Nessun crash; limitazione Windows rispettata e console funzionante. |
| AC-21 | Eseguire `strazio.exe scarica --guida` | Aiuto specifico del sottocomando in italiano; `--guida` usato in tutti gli esempi e riferimenti, senza opzioni di help in inglese. |
| AC-22 | Salvare e riordinare un XLSX | Stile blu `#0000FF` e grassetto di tutte le celle Oro e Doppio Oro conservato dopo `ordina-asc` e `ordina-desc`. |

## 14. Rischi, dipendenze e decisioni aperte

1. **Sito di terzi.** L'HTML e i permessi d'accesso possono cambiare; ogni adattatore deve avere test di contratto e gestione esplicita dell'errore. La pagina è stata consultata per la presente specifica, ma il DOM grezzo completo va controllato con il fixture prima di implementare i selettori definitivi.
2. **Secondo adattatore.** Il fallback è progettato ma non sarà operativo finché non viene scelto e sviluppato un'altra fonte compatibile.
3. **Extra storici.** La fonte dichiara che Extra parte dal 10 settembre 2020; i giorni precedenti saranno archiviati con le 15 celle Extra vuote.
4. **Data italiana.** Le giornate e i concorsi seguono `Europe/Rome`, indipendentemente dal fuso orario del computer Windows; il concorso 288 della pagina campione è etichettato `00:00` ma resta assegnato dalla fonte alla data richiesta.
5. **Notifiche elevate.** Windows non consente i toast dal processo amministratore. La funzione fondamentale rimane il download; non aggirare le restrizioni di sicurezza.
6. **Schedulazione a PC spento.** Un task non può scaricare mentre il PC è spento: eseguire il recupero al successivo avvio/accesso e mostrare lo stato nella CLI.
7. **Distribuzione single-file.** Richiede test reali su macchine pulite, inclusa la funzione notifiche.
8. **Conferme.** `ordina-*` riordina file esistenti dopo conferma. In uso automatizzato va sempre selezionato un comportamento esplicito che non si blocchi su input.

## 15. Riferimenti tecnici

- Esempio dell'archivio: https://www.10elotto5.it/archivio-10elotto5-minuti/?data=2026-10-01
- Supporto .NET 10 LTS: https://learn.microsoft.com/en-us/dotnet/core/releases-and-support
- Pubblicazione single-file .NET: https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview
- Notifiche Windows nelle console app: https://learn.microsoft.com/en-us/windows/apps/develop/notifications/app-notifications/app-notifications-console
- Utilità di pianificazione (`schtasks` e opzioni task): https://learn.microsoft.com/en-us/windows-server/administration/windows-commands/schtasks-create
- ClosedXML: https://docs.closedxml.io/en/latest/

---

**Deliverable successivo (fuori dall'ambito di questi due file):** implementazione della solution .NET, test automatici con HTML campione, build `strazio.exe`, e prova su Windows pulito. Il catalogo di frasi allegato è un asset *di build*, non un file da distribuire all'utente finale.
