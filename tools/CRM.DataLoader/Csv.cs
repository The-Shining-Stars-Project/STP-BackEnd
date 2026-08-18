namespace CRM.DataLoader;

/// <summary>
/// Minimal RFC 4180 reader. Deliberately hand-rolled: the solution has no CSV or
/// spreadsheet package, and a throwaway migration tool is the last place to add a
/// dependency the client's developer would then inherit. Quoted fields, embedded commas,
/// embedded newlines and doubled quotes are all handled — which is the whole spec that
/// matters for a sheet exported from Excel.
/// </summary>
public static class Csv
{
    public static List<string[]> Parse(string text)
    {
        var rows = new List<string[]>();
        var field = new System.Text.StringBuilder();
        var row = new List<string>();
        var inQuotes = false;

        // Normalise line endings first so a CRLF export and an LF export parse identically.
        text = text.Replace("\r\n", "\n").Replace('\r', '\n');

        for (var i = 0; i < text.Length; i++)
        {
            var c = text[i];

            if (inQuotes)
            {
                if (c == '"')
                {
                    // "" inside a quoted field is a literal quote.
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

        // Trailing field / row when the file does not end in a newline.
        if (field.Length > 0 || row.Count > 0)
        {
            row.Add(field.ToString());
            rows.Add(row.ToArray());
        }

        // Excel pads short rows out with empty cells inconsistently; drop rows that are
        // entirely blank rather than importing a ghost participant.
        return rows.Where(r => r.Any(c => !string.IsNullOrWhiteSpace(c))).ToList();
    }
}
