using ClosedXML.Excel;

namespace Yazaki.CommandeChaine.Desktop.Services;

public sealed class FOImportService
{
    private static readonly string[] ReferenceHeaders = { "reference", "ref", "harnessref" };
    private static readonly string[] ProductionHeaders = { "productiontimeminutes", "productiontimeinminutes", "productiontime", "prodtime", "prodtime_min" };
    private static readonly string[] LateHeaders = { "lateflag", "late", "islate" };
    private static readonly string[] UrgentHeaders = { "urgent", "isurgent" };

    public List<FOHarnessRow> LoadFromExcel(string filePath)
    {
        using var workbook = new XLWorkbook(filePath);
        var sheet = workbook.Worksheets.First();

        var headerRow = sheet.FirstRowUsed();
        if (headerRow is null)
        {
            return new List<FOHarnessRow>();
        }
        var headerMap = BuildHeaderMap(headerRow);

        var rows = new List<FOHarnessRow>();
        var order = 1;

        foreach (var row in sheet.RowsUsed().Skip(1))
        {
            var reference = GetCell(row, headerMap, ReferenceHeaders);
            var prodText = GetCell(row, headerMap, ProductionHeaders);

            if (string.IsNullOrWhiteSpace(reference) || string.IsNullOrWhiteSpace(prodText))
            {
                continue;
            }

            var prod = int.TryParse(prodText.Trim(), out var prodVal) ? prodVal : 0;
            var lateText = GetCell(row, headerMap, LateHeaders);
            var urgentText = GetCell(row, headerMap, UrgentHeaders);

            var item = new FOHarnessRow
            {
                OrderIndex = order++,
                Reference = reference.Trim(),
                ProductionTimeMinutes = Math.Max(0, prod),
                IsLate = ParseBool(lateText),
                IsUrgent = ParseBool(urgentText)
            };

            rows.Add(item);
        }

        return rows;
    }

    private static Dictionary<string, int> BuildHeaderMap(IXLRow headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        foreach (var cell in headerRow.CellsUsed())
        {
            var key = cell.GetString().Trim();
            if (!string.IsNullOrWhiteSpace(key) && !map.ContainsKey(key))
            {
                map[key] = cell.Address.ColumnNumber;
            }
        }

        return map;
    }

    private static string GetCell(IXLRow row, Dictionary<string, int> map, IEnumerable<string> keys)
    {
        foreach (var key in keys)
        {
            if (map.TryGetValue(key, out var col))
            {
                return row.Cell(col).GetString();
            }
        }

        return string.Empty;
    }

    private static bool ParseBool(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return false;
        }

        var value = text.Trim().ToLowerInvariant();
        return value is "1" or "true" or "yes" or "y";
    }
}
