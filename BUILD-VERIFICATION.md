# Verifica della build — 17 ottobre 2026

Eseguibile: `artifacts/win-x64/strazio.exe`, Windows x64, single-file self-contained .NET 10, non firmato.

SHA-256: `2FCA858A305F5E2B0E28DBA64F099C0A2B6AC6BA1C5D83C4A2DDA3179146D915`.

## Risultati ottenuti

- Compilazione e pubblicazione Release senza avvisi o errori.
- 85 test riusciti: 16 Domain, 18 Application, 51 Integration. I test sono offline e non creano task o notifiche reali.
- Verifica della cartella pubblicata: un solo file, `strazio.exe`; nessun catalogo frasi distribuito.
- Avvio dell'EXE pubblicato con `--guida`, `scarica --guida` e `stato`: guida italiana e stato del task non configurato.
- Download reale del 1 ottobre 2026: file valido `artifacts/smoke-archive/2026-10-01.xlsx`, 288 concorsi. Seconda esecuzione: `Già presente`, senza nuova richiesta alla fonte.
- Le prove offline con 1, 96 e 287 concorsi producono esattamente 1, 96 e 287 righe. Un concorso mancante non viene inventato. Anche numerazioni discontinue vengono accettate.
- Schema Excel, valori dell'esempio, colori e grassetto U/V verificati dopo creazione e riordini ascendente e discendente.
- File bloccato da un handle esclusivo: sostituzione fallita e originale identico byte per byte.
- `auto-archivia` senza elevazione: istruzioni con percorso reale, codice 1, nessun task o impostazione di scheduling creati.
- Prova manuale del toast sul computer di sviluppo: avvio interattivo dell'EXE, identità e collegamento Start presenti, una notifica registrata nella cronologia Windows. La prova non ha salvato il testo della frase in un file di diagnostica.

## Limiti della verifica

Il computer di sviluppo ha runtime .NET installati. Non è stata eseguita una prova su una VM Windows pulita. La registrazione reale del Task Scheduler con elevazione, le policy di sessione, l'interruzione forzata del processo durante il commit e l'esaurimento fisico di un volume richiedono le verifiche isolate di [RELEASE-CHECKLIST.md](RELEASE-CHECKLIST.md). La procedura guidata di scheduling e il recupero immediato sono testati con un adattatore Scheduler sostitutivo, senza creare task nell'account di sviluppo.
