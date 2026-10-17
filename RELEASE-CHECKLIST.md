# Verifiche di rilascio

L'eseguibile è pubblicato per Windows x64. Il computer di sviluppo ha runtime .NET già installati: una prova qui non equivale a una prova su Windows pulito. Non dichiarare completata la certificazione seguente senza eseguirla in una VM separata.

- [ ] Copiare soltanto `strazio.exe` in Windows 10/11 x64 supportato, senza .NET, Excel o Windows App SDK installati separatamente. Eseguire `--guida`, `stato` e un download.
- [ ] Verificare la visualizzazione del toast da un account non elevato con notifiche abilitate; controllare che la notifica non venga tentata con elevazione. Verificare l'assenza di un catalogo frasi estratto nella cartella di distribuzione o nei temporanei dell'app.
- [ ] Eseguire il menu `archivia-qui` per tutte le tre scelte, conferme negative, percorso inesistente, percorso protetto, variabili ambiente, spazi e percorso che indica un file.
- [ ] Eseguire `auto-archivia` senza elevazione: solo istruzioni, nessun task o impostazione di scheduling modificata.
- [ ] Con un account di prova e privilegi elevati, configurare `auto-archivia`. Verificare task giornaliero e all'accesso, account utente corretto, esecuzione senza elevazione, directory assoluta, recupero immediato e al successivo accesso. Non registrare un task reale durante i normali test automatici.
- [ ] Con Excel o un altro lettore, verificare assenza di intestazioni, un solo foglio, numero reale di righe, C:T senza entrambi i numeri speciali, U/V blu grassetto, W:X vuote, Y/Z valori autonomi e 15 Extra. Ripetere dopo riordino.
- [ ] Su un volume di prova piccolo, esaurire lo spazio durante il salvataggio e verificare che l'originale rimanga intatto. Ripetere con cartella senza permessi e file aperto in Excel. I test automatici coprono file bloccati e commit fallito, ma non simulano l'esaurimento fisico di un volume.
- [ ] Terminare il processo durante il salvataggio su un archivio di prova: nessun file giornaliero parziale; dopo riavvio, recupero dei giorni assenti; eventuali temporanei sono solo in `.strazio-work`.
- [ ] Verificare firma Authenticode o informare il destinatario che l'EXE non è firmato; non consigliare di disabilitare SmartScreen.
