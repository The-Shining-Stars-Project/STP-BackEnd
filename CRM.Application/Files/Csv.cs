using System.Text;

namespace CRM.Application.Files;

/// <summary>
/// Minimal RFC 4180 reader — the same hand-rolled parser the one-off data loader used,
/// now in the application layer so the self-serve import shares it. Quoted fields, embedded
/// commas, embedded newlines and doubled quotes are handled, which is the whole spec that
/// matters for a sheet exported from Excel or Google Sheets. No dependency to inherit.
/// </summary>
public static class Csv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var field = new StringBuilder();
        var row = new List<string>();
        var inQuotes = false;

        // Strip a UTF-8 BOM (Excel writes one) and normalise line endings so CRLF and LF
        // exports parse identically.
        if (text.Length > 0 && text[0] == '\uFEFF') text = text[1..];
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    if (i + 1 < text.Length && text[i + 1] == '"') { field.Append('"'); i++; }
                    else inQuotes = false;
                }
                else field.Append(c);
                continue;
            }

            switch (c)
            {
                case '"':
                    inQuotes = true;
                    break;
                case ',':
                    row.Add(field.ToString()); field.Clear();
                    break;
                case '\n':
                    row.Add(field.ToString()); field.Clear();
                    rows.Add(row.ToArray()); row = new List<string>();
                    break;
                default:
                    field.Append(c);
                    break;
            }
        }

        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        // Excel pads short rows out with empty cells inconsistently; drop rows that are
        // entirely blank rather than importing a ghost.
        return rows.Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
    }

    /// <summary>One CSV line with RFC 4180 quoting, for templates and exports.</summary>
    public static string Line(IEnumerable<string> cells) =>
        string.Join(",", cells.Select(c => $"\"{c.Replace("\"", "\"\"")}\""));
}
