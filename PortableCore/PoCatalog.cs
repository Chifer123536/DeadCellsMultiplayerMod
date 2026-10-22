using System.Text;

namespace DeadCellsMultiplayerMod.PortableCore;

internal sealed class PoCatalog
{
    readonly Dictionary<string, string> _entries;

    PoCatalog(Dictionary<string, string> entries)
    {
        _entries = entries;
    }

    public static PoCatalog Empty { get; } = new(new Dictionary<string, string>(StringComparer.Ordinal));

    public int Count => _entries.Count;

    public bool TryGet(string key, out string value)
    {
        if (key.Length > 0 && _entries.TryGetValue(key, out var found) && found.Length > 0)
        {
            value = found;
            return true;
        }

        value = string.Empty;
        return false;
    }

    public static PoCatalog Parse(string text)
    {
        var entries = new Dictionary<string, string>(StringComparer.Ordinal);
        var id = new StringBuilder();
        var str = new StringBuilder();
        var target = Field.None;
        var fuzzy = false;
        var hasEntry = false;

        void Flush()
        {
            if (hasEntry && !fuzzy && id.Length > 0)
                entries[id.ToString()] = str.ToString();
            id.Clear();
            str.Clear();
            target = Field.None;
            fuzzy = false;
            hasEntry = false;
        }

        using var reader = new StringReader(text);
        string? raw;
        while ((raw = reader.ReadLine()) != null)
        {
            var line = raw.Trim();
            if (line.Length == 0)
            {
                Flush();
                continue;
            }

            if (line.StartsWith("#,", StringComparison.Ordinal))
            {
                if (target == Field.Str)
                    Flush();
                fuzzy |= line.Contains("fuzzy", StringComparison.Ordinal);
                continue;
            }

            if (line[0] == '#')
                continue;

            if (line.StartsWith("msgid ", StringComparison.Ordinal))
            {
                if (target == Field.Str)
                    Flush();
                hasEntry = true;
                target = Field.Id;
                AppendQuoted(id, line.AsSpan(6));
                continue;
            }

            if (line.StartsWith("msgstr ", StringComparison.Ordinal))
            {
                target = Field.Str;
                AppendQuoted(str, line.AsSpan(7));
                continue;
            }

            if (line[0] == '"')
            {
                if (target == Field.Id)
                    AppendQuoted(id, line);
                else if (target == Field.Str)
                    AppendQuoted(str, line);
            }
        }

        Flush();
        return new PoCatalog(entries);
    }

    static void AppendQuoted(StringBuilder sb, ReadOnlySpan<char> token)
    {
        token = token.Trim();
        if (token.Length < 2 || token[0] != '"' || token[^1] != '"')
            return;

        token = token[1..^1];
        for (var i = 0; i < token.Length; i++)
        {
            var c = token[i];
            if (c != '\\' || i + 1 >= token.Length)
            {
                sb.Append(c);
                continue;
            }

            i++;
            sb.Append(token[i] switch
            {
                'n' => '\n',
                't' => '\t',
                'r' => '\r',
                '"' => '"',
                '\\' => '\\',
                var other => other
            });
        }
    }

    enum Field
    {
        None,
        Id,
        Str
    }
}

/*
 * ═══════════════════════════════════════
 *  PO CATALOG — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Разбор gettext .po: msgid/msgstr с многострочными продолжениями и экранированием \n \t \" \\.
 *  Записи с флагом fuzzy и пустые переводы игнорируются (TryGet → false, вызывающий берёт ключ).
 *  Без зависимостей от игры — покрыт тестами.
 * ═══════════════════════════════════════
*/
