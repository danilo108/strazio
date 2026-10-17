using System.CommandLine;

namespace Strazio.Cli;

internal sealed class CommandLine
{
    public RootCommand Root { get; } = new("STRAZIO — Archivio 10eLotto ogni 5 minuti");
    public Option<bool> Guide { get; } = new("--guida") { Recursive = true };
    public Option<bool> NonInteractive { get; } = new("--non-interattivo") { Recursive = true, Hidden = true };
    public Option<bool> Yes { get; } = new("--si") { Recursive = true };
    public Option<string?> Date { get; } = new("--data");
    public Option<string?> From { get; } = new("--dal");
    public Option<string?> To { get; } = new("--al");
    public Option<string?> Folder { get; } = new("--cartella");
    public Option<bool> Force { get; } = new("--forza");

    public CommandLine()
    {
        Root.Options.Clear(); // Remove the default English help/version options entirely.
        Root.Options.Add(Guide);
        Root.Options.Add(NonInteractive);
        Root.Options.Add(Yes);
        Root.SetAction(_ => { }); // A root action permits guide-only and empty invocations.
        foreach (var name in new[] { "archivia-qui", "scarica", "auto-archivia", "ordina-asc", "ordina-desc", "basta-messaggi", "vabbe", "stato", "__esegui-pianificato" })
        {
            var command = new Command(name) { Hidden = name.StartsWith("__", StringComparison.Ordinal) };
            if (name == "scarica")
            {
                command.Options.Add(Date);
                command.Options.Add(From);
                command.Options.Add(To);
                command.Options.Add(Folder);
                command.Options.Add(Force);
            }
            Root.Subcommands.Add(command);
        }
    }

    public static void PrintGuide(string? command = null)
    {
        Console.WriteLine("STRAZIO — Archivio 10eLotto ogni 5 minuti\n");
        if (command == "scarica")
        {
            Console.WriteLine("Uso: strazio.exe scarica [opzioni]\n\n" +
                "  --data <giorno>     Scarica un solo giorno.\n" +
                "  --dal <giorno>      Primo giorno dell'intervallo.\n" +
                "  --al <giorno>       Ultimo giorno, incluso.\n" +
                "  --cartella <nome>   Usa questa cartella soltanto per questa esecuzione.\n" +
                "  --forza            Autorizza la sostituzione dei file, con copia preventiva.\n" +
                "  --si               Conferma anche intervalli superiori a 31 giorni.\n" +
                "  --guida            Mostra questa guida.\n\n" +
                "Date: 01/10/2026, 1/10/2026, 2026-10-01, oggi oppure ieri (data italiana).\n" +
                "--data non si usa insieme a --dal o --al. Senza date parte la procedura guidata.\n" +
                "I file validi già presenti vengono saltati; quelli provvisori possono essere aggiornati.\n" +
                "Salvo solo i concorsi disponibili, anche meno di 288, senza inventare righe.\n\n" +
                "Esempio: strazio.exe scarica --data 2026-10-01 --cartella \"D:\\Lotto\"");
            return;
        }
        if (!string.IsNullOrEmpty(command) && command != "strazio")
        {
            var description = command switch
            {
                "archivia-qui" => "Scegli e conferma la cartella predefinita tramite un menu con tre scelte.",
                "auto-archivia" => "Apri come amministratore. Scegli cartella, prima data e orario locale; configuro il task e avvio subito il recupero.",
                "ordina-asc" => "Salva l'ordine crescente e riordina gli Excel giornalieri validi dopo conferma. --si conferma il numero dei file.",
                "ordina-desc" => "Salva l'ordine decrescente e riordina gli Excel giornalieri validi dopo conferma. --si conferma il numero dei file.",
                "basta-messaggi" => "Disabilita subito le frasi in console e le notifiche affettuose.",
                "vabbe" => "Riabilita le frasi e ne mostra immediatamente una.",
                "stato" => "Mostra cartella, ordinamento, messaggi, ultimo file valido e stato del task.",
                _ => "Comando interno riservato all'Utilità di pianificazione."
            };
            Console.WriteLine($"Uso: strazio.exe {command}\n\n{description}\n\n--guida mostra questa guida.");
            return;
        }
        Console.WriteLine("Uso:\n  strazio.exe <comando> [opzioni]\n\nComandi:\n" +
            "  archivia-qui      Scegli dove conservare i file Excel.\n" +
            "  scarica           Scarica un giorno o più giorni.\n" +
            "  auto-archivia     Prepara il salvataggio automatico giornaliero.\n" +
            "  ordina-asc        Metti le estrazioni dalla più vecchia alla più recente.\n" +
            "  ordina-desc       Metti le estrazioni dalla più recente alla più vecchia.\n" +
            "  basta-messaggi    Non mostrare più le frasi affettuose.\n" +
            "  vabbe             Mostra di nuovo le frasi affettuose.\n" +
            "  stato             Controlla come è configurato Strazio.\n\n" +
            "Esempi:\n  strazio.exe archivia-qui\n  strazio.exe scarica --data 2026-10-01\n" +
            "  strazio.exe scarica --dal 2026-10-01 --al 2026-10-07\n  strazio.exe auto-archivia\n\n" +
            "Per maggiori informazioni: strazio.exe <comando> --guida\n\n" +
            "I file si chiamano yyyy-MM-dd.xlsx e contengono soltanto i concorsi disponibili.\n" +
            "Le date seguono l'Italia; l'orario del task segue il computer.\n" +
            "Windows può conservare le notifiche nella propria cronologia.\n" +
            "Un eseguibile non firmato può mostrare un avviso di Windows SmartScreen: verifica sempre la provenienza.");
    }
}
