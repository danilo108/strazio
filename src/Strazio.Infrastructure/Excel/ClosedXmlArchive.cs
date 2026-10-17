using ClosedXML.Excel;
using Strazio.Application;
using Strazio.Domain;
using Strazio.Infrastructure.Storage;

namespace Strazio.Infrastructure.Excel;

public sealed class ClosedXmlArchive : IArchiveWriter, IArchiveInspector
{
    public const string SheetName = "bravo figlio";
    private const string ProvisionalProperty = "Strazio.Provisional";

    public Task WriteAsync(DrawDay day, string destination, SortOrder order, CancellationToken ct)
    {
        DrawValidation.Validate(day);
        using var book = new XLWorkbook();
        var sheet = book.Worksheets.Add(SheetName);
        sheet.Columns(1, 41).Width = 5;
        sheet.Columns(1, 1).Width = 13;
        sheet.Columns(25, 25).Width = 13;
        sheet.Columns(2, 2).Width = 7;
        sheet.Columns(26, 26).Width = 7;
        sheet.Style.NumberFormat.Format = "0";
        var rows = day.Draws.Select(DrawValidation.Normalize);
        rows = order == SortOrder.Asc ? rows.OrderBy(r => r.CompetitionNumber) : rows.OrderByDescending(r => r.CompetitionNumber);
        int rowIndex = 0;
        foreach (var row in rows)
        {
            ct.ThrowIfCancellationRequested();
            rowIndex++;
            foreach (var column in new[] { 1, 25 })
            {
                sheet.Cell(rowIndex, column).Value = row.Date.ToDateTime(TimeOnly.MinValue);
                sheet.Cell(rowIndex, column).Style.NumberFormat.Format = "dd/MM/yyyy";
            }
            sheet.Cell(rowIndex, 2).Value = row.CompetitionNumber;
            sheet.Cell(rowIndex, 26).Value = row.CompetitionNumber;
            for (var i = 0; i < 18; i++) sheet.Cell(rowIndex, i + 3).Value = row.MainNumbers18[i];
            sheet.Cell(rowIndex, 21).Value = row.Oro;
            sheet.Cell(rowIndex, 22).Value = row.DoppioOroSecondNumber;
            foreach (var column in new[] { 21, 22 })
            {
                sheet.Cell(rowIndex, column).Style.Font.Bold = true;
                sheet.Cell(rowIndex, column).Style.Font.FontColor = XLColor.FromHtml("#0000FF");
            }
            if (row.ExtraNumbers15 is not null)
                for (var i = 0; i < 15; i++) sheet.Cell(rowIndex, i + 27).Value = row.ExtraNumbers15[i];
        }
        book.CustomProperties.Add(ProvisionalProperty, day.IsProvisional);
        SaveVerified(book, destination, day.Date, order, ct);
        return Task.CompletedTask;
    }

    public ArchiveFile Inspect(string path, DateOnly date)
    {
        try
        {
            using var book = new XLWorkbook(path);
            Validate(book, date, null);
            var provisional = book.CustomProperties.Any(p => p.Name == ProvisionalProperty) && book.CustomProperty(ProvisionalProperty).GetValue<bool>();
            return new(date, path, true, provisional);
        }
        catch (Exception ex) when (ex is not OutOfMemoryException)
        {
            return new(date, path, false, false, $"File non valido o non accessibile: {ex.Message}");
        }
    }

    public Task ReorderAsync(string path, DateOnly date, SortOrder order, CancellationToken ct)
    {
        using var book = new XLWorkbook(path);
        Validate(book, date, null);
        var sheet = book.Worksheet(1);
        var count = sheet.LastRowUsed(XLCellsUsedOptions.Contents)!.RowNumber();
        sheet.Range(1, 1, count, 41).Sort(2, order == SortOrder.Asc ? XLSortOrder.Ascending : XLSortOrder.Descending);
        SaveVerified(book, path, date, order, ct);
        return Task.CompletedTask;
    }

    private static void SaveVerified(XLWorkbook book, string destination, DateOnly date, SortOrder order, CancellationToken ct)
    {
        Validate(book, date, order);
        destination = Path.GetFullPath(destination);
        var workDirectory = Path.Combine(Path.GetDirectoryName(destination)!, ".strazio-work");
        Directory.CreateDirectory(workDirectory);
        var temporary = Path.Combine(workDirectory, $"{Guid.NewGuid():N}.xlsx");
        try
        {
            using (var memory = new MemoryStream())
            {
                book.SaveAs(memory);
                ct.ThrowIfCancellationRequested();
                using var file = new FileStream(temporary, FileMode.CreateNew, FileAccess.Write, FileShare.None);
                memory.Position = 0;
                memory.CopyTo(file);
                file.Flush(true);
            }
            using (var verification = new XLWorkbook(temporary)) Validate(verification, date, order);
            ct.ThrowIfCancellationRequested();
            AtomicFiles.Commit(temporary, destination, backup: true);
        }
        finally { if (File.Exists(temporary)) File.Delete(temporary); }
    }

    private static void Validate(XLWorkbook book, DateOnly date, SortOrder? order)
    {
        if (book.Worksheets.Count != 1 || book.Worksheet(1).Name != SheetName || book.Worksheet(1).Visibility != XLWorksheetVisibility.Visible)
            throw new InvalidDataException("Serve un unico foglio visibile chiamato bravo figlio.");
        var sheet = book.Worksheet(1);
        if (sheet.Tables.Any() || sheet.AutoFilter.IsEnabled || sheet.MergedRanges.Any()) throw new InvalidDataException("Il foglio contiene tabelle, filtri o celle unite.");
        var lastRow = sheet.LastRowUsed(XLCellsUsedOptions.Contents)?.RowNumber() ?? 0;
        var lastColumn = sheet.LastColumnUsed(XLCellsUsedOptions.Contents)?.ColumnNumber() ?? 0;
        if (lastRow is < 1 or > 288 || lastColumn > 41) throw new InvalidDataException("Dimensioni del foglio non valide.");
        if (sheet.CellsUsed().Any(c => c.HasFormula || c.HasComment)) throw new InvalidDataException("Il foglio contiene formule o commenti.");
        var competitions = new List<int>();
        for (var r = 1; r <= lastRow; r++)
        {
            foreach (var col in new[] { 1, 25 })
            {
                var cell = sheet.Cell(r, col);
                if (cell.DataType != XLDataType.DateTime || cell.GetDateTime() != date.ToDateTime(TimeOnly.MinValue) ||
                    cell.Style.NumberFormat.Format != "dd/MM/yyyy") throw new InvalidDataException("Data Excel non valida.");
            }
            var competition = Number(sheet.Cell(r, 2));
            if (competition != Number(sheet.Cell(r, 26))) throw new InvalidDataException("Le colonne B e Z non coincidono.");
            competitions.Add(competition);
            if (!sheet.Cell(r, 23).IsEmpty() || !sheet.Cell(r, 24).IsEmpty()) throw new InvalidDataException("W e X devono essere vuote.");
            var main = Enumerable.Range(3, 18).Select(c => Number(sheet.Cell(r, c))).ToArray();
            var oro = Number(sheet.Cell(r, 21));
            var second = Number(sheet.Cell(r, 22));
            var extraCells = Enumerable.Range(27, 15).Select(c => sheet.Cell(r, c)).ToArray();
            var extra = extraCells.All(c => c.IsEmpty()) ? null : extraCells.Select(Number).ToArray();
            DrawValidation.Validate(new Draw(date, competition, main.Concat([oro, second]), oro, second, extra));
            foreach (var col in new[] { 21, 22 })
            {
                var cell = sheet.Cell(r, col);
                var color = cell.Style.Font.FontColor;
                if (!cell.Style.Font.Bold || color.ColorType != XLColorType.Color || color.Color.ToArgb() != System.Drawing.Color.Blue.ToArgb() ||
                    cell.Style.NumberFormat.Format != "0") throw new InvalidDataException("Oro e Doppio Oro devono avere carattere blu puro, grassetto e formato intero.");
            }
        }
        if (competitions.Distinct().Count() != competitions.Count) throw new InvalidDataException("Concorsi duplicati nel file Excel.");
        if (order is not null && !competitions.SequenceEqual(order == SortOrder.Asc ? competitions.Order() : competitions.OrderDescending()))
            throw new InvalidDataException("Ordinamento Excel non valido.");
    }

    private static int Number(IXLCell cell)
    {
        if (cell.DataType != XLDataType.Number) throw new InvalidDataException("Era atteso un numero intero.");
        var value = cell.GetDouble();
        if (value != Math.Truncate(value) || value < 1 || value > 288) throw new InvalidDataException("Numero intero non valido.");
        return (int)value;
    }
}
