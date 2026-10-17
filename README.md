# Strazio

CLI Windows in italiano per archiviare le estrazioni del 10eLotto ogni 5 minuti. Implementa [la specifica](SPECIFICA_TECNICA_STRAZIO.md), con dominio e servizi indipendenti da HTTP, HTML, Excel e Windows.

## Uso

La distribuzione x64 è `artifacts/win-x64/strazio.exe`. Copia soltanto questo eseguibile in una cartella stabile. Non occorrono Excel, .NET o un compilatore. È richiesta una versione Windows 10/11 supportata da .NET 10 (Windows 10 22H2 o successiva). L'eseguibile non è firmato: Windows SmartScreen può chiedere una verifica della provenienza.

In PowerShell:

```powershell
.\strazio.exe --guida
.\strazio.exe archivia-qui
.\strazio.exe scarica --data 2026-10-01
.\strazio.exe scarica --dal 2026-10-01 --al 2026-10-07 --cartella 'D:\Lotto'
.\strazio.exe ordina-asc
.\strazio.exe basta-messaggi
.\strazio.exe vabbe
.\strazio.exe stato
```

Le date accettano `01/10/2026`, `1/10/2026`, `2026-10-01`, `oggi` e `ieri`. Le giornate seguono il fuso italiano, anche su computer in un altro fuso. L'intervallo include entrambi gli estremi.

Ogni file `yyyy-MM-dd.xlsx` ha un solo foglio, `bravo figlio`, senza intestazioni. Contiene **esattamente i concorsi disponibili**, da uno a 288: una giornata con 96 concorsi produce 96 righe. Le numerazioni possono avere salti. Il programma non completa né inventa le estrazioni mancanti. I numeri principali mantengono l'ordine della fonte; Oro e secondo Doppio Oro vanno in U/V, con carattere blu puro e grassetto. Prima del 10 settembre 2020 le celle Extra rimangono vuote.

`--cartella` vale solo per il download corrente. `--forza` autorizza la sostituzione di file già presenti, anche corrotti, con backup. I file definitivi validi vengono normalmente saltati; quelli del giorno corrente sono provvisori e vengono aggiornati in seguito. Lo stato provvisorio è memorizzato nelle proprietà del workbook, senza aggiungere celle o fogli.

Le impostazioni si trovano in `%LOCALAPPDATA%\Strazio\settings.json`. Il catalogo delle frasi è incorporato nell'assembly: non distribuirlo insieme all'eseguibile. Frasi e toast si possono disabilitare con `basta-messaggi`. Per le notifiche viene usata l'API nativa Windows `Windows.UI.Notifications`, alternativa ammessa dalla specifica per evitare un runtime Windows App SDK esterno. Il primo toast registra l'identità dell'app nel profilo utente e crea il collegamento Strazio nel menu Start. Le notifiche dipendono dalle impostazioni e dalla sessione Windows; non vengono tentate nei processi elevati. Windows può conservarne il testo nella propria cronologia.

## Automazione

Apri PowerShell come amministratore ed esegui dall'eseguibile definitivo:

```powershell
& 'C:\Strazio\strazio.exe' auto-archivia
```

La procedura conferma cartella, prima data, buchi da recuperare e orario **locale del computer**, registra il task COM `Strazio - Archivio 10eLotto` e avvia subito il recupero. Il task usa l'account corrente senza elevazione, ogni giorno e all'accesso; esegue `__esegui-pianificato --non-interattivo`, recuperando tutte le giornate mancanti dalla data iniziale fino a ieri in Italia. Non scarica automaticamente il giorno in corso. Non spostare l'eseguibile dopo la configurazione.

La modalità configurata è «solo quando l'utente ha effettuato l'accesso». Non vengono richieste o memorizzate password. S4U non viene usato perché non consente l'accesso alla rete. Se l'elevazione usa un altro account amministratore, vengono usati il profilo e l'archivio di quell'account: il programma lo segnala prima della configurazione.

Per script, aggiungi `--non-interattivo` e tutti i parametri obbligatori. Per più di 31 giorni o per riordinare file esistenti serve anche `--si`. Per creare una cartella mancante senza domande serve `--si`. I file corrotti vengono conservati nei batch e segnalati; controllali e usa `scarica --forza` solo se intendi sostituirli. Le invocazioni non interattive non mostrano frasi né toast.

Codici di uscita: `0` operazione riuscita, `1` errore operativo o fallimento parziale, `2` sintassi CLI non valida, `130` interruzione. I giorni non disponibili sono contati separatamente; non vengono creati file vuoti.

## Sicurezza dei file e della fonte

Le modifiche ai workbook vengono preparate in memoria, salvate in `.strazio-work` sullo stesso volume e riaperte per la convalida prima della sostituzione atomica. Prima di sostituire o riordinare un file, viene conservata una copia in `.strazio-backups`. Non eliminare i backup finché non hai controllato il risultato. Un arresto improvviso può lasciare un file con nome casuale in `.strazio-work`, mai un archivio giornaliero pubblicato a metà. Puoi rimuovere i temporanei quando nessun processo Strazio è in esecuzione. Un mutex per cartella impedisce sovrapposizioni anche tra sessioni Windows.

La prima versione ha un solo adattatore operativo, `10elotto5.it-html`; non simula una seconda fonte. Gli adattatori si registrano tramite `IExtractionSourceProvider`. Il client controlla robots.txt, usa un'identità dichiarata, richiede solo HTML e limita le risposte a 12 MB. I download sono sequenziali, con pausa casuale di 3–8 secondi, timeout di 30 secondi e al massimo due tentativi per guasto transitorio. HTTP 403/401, 429 o challenge interrompono il batch senza fallback aggirante. `Retry-After` viene segnalato e conservato tra avvii nel file tecnico `%LOCALAPPDATA%\Strazio\source-cooldown.json`; senza un valore utile, HTTP 429 sospende la fonte per un'ora. robots.txt della fonte, consultato il 17 ottobre 2026, non vietava il percorso archivio; sulla pagina campione non erano presenti collegamenti a una licenza o condizioni di riutilizzo. Questo non costituisce una licenza dei dati: prima di una distribuzione o riutilizzazione diversa dall'archivio personale, verificare i termini del titolare e preferire una fonte ufficiale/licenziata se disponibile.

## Sviluppo e verifica

Serve il SDK .NET 10 per compilare i sorgenti, C# 14. I quattro progetti separano Domain, Application, Infrastructure (HTTP, Excel, configurazione, Windows) e CLI. I tre progetti di test non effettuano chiamate a Internet e non registrano task o notifiche reali. Il fixture HTML del 1 ottobre 2026 è una risposta della fonte pubblica acquisita per verificare il parser; le varianti parziali sono derivate soltanto nei test, senza inventare risultati di gioco.

```powershell
dotnet test .\Strazio.sln -c Release
.\scripts\publish.ps1
# Oppure con il SDK locale usato in questo ambiente:
.\scripts\publish.ps1 -Dotnet "$env:LOCALAPPDATA\StrazioBuild\dotnet\dotnet.exe"
```

La pubblicazione è self-contained, senza trimming e con runtime nativo incorporato; i simboli di debug sono embedded. La cartella di distribuzione deve contenere un solo EXE. Il runtime può estrarre componenti nativi nella cartella temporanea di Windows; il catalogo delle frasi non viene estratto intenzionalmente né scritto nei log o nelle preferenze.

Le prove automatiche coprono mapping reale, giorni da 1/96/287/288 concorsi, salti e duplicati, date, rifiuto di HTML incongruente, fallback, blocchi e rate limit, recupero di buchi, aggiornamento dei provvisori, backup, file bloccati, cancellazione e schema/stili dopo entrambi i riordinamenti. I risultati della build consegnata sono in [BUILD-VERIFICATION.md](BUILD-VERIFICATION.md); le istruzioni della verifica finale su macchina pulita sono in [RELEASE-CHECKLIST.md](RELEASE-CHECKLIST.md).

Riferimenti di implementazione: [pubblicazione single-file .NET](https://learn.microsoft.com/en-us/dotnet/core/deploying/single-file/overview), [identità e shortcut per toast desktop](https://learn.microsoft.com/en-us/windows/win32/shell/enable-desktop-toast-with-appusermodelid), [tipi di accesso del Task Scheduler](https://learn.microsoft.com/en-us/windows/win32/taskschd/principal-logontype).
