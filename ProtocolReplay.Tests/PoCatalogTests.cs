using DeadCellsMultiplayerMod.PortableCore;
using Xunit;

namespace DeadCellsMultiplayerMod.ProtocolReplay.Tests;

public sealed class PoCatalogTests
{
    [Fact]
    public void ParsesSimpleAndMultilineEntries()
    {
        const string po = "msgid \"\"\nmsgstr \"\"\n\"Language: ru\\n\"\n\n" +
                          "#: a.cs:1\nmsgid \"Back\"\nmsgstr \"Назад\"\n\n" +
                          "msgid \"\"\n\"Long \"\n\"key\"\nmsgstr \"\"\n\"Длинный \"\n\"ключ\"\n";

        var catalog = PoCatalog.Parse(po);

        Assert.Equal(2, catalog.Count);
        Assert.True(catalog.TryGet("Back", out var back));
        Assert.Equal("Назад", back);
        Assert.True(catalog.TryGet("Long key", out var longValue));
        Assert.Equal("Длинный ключ", longValue);
    }

    [Fact]
    public void UnescapesQuotesNewlinesAndBackslashes()
    {
        const string po = "msgid \"Say \\\"hi\\\"\\nnow\\\\\"\nmsgstr \"Скажи «hi»\\nсейчас\\\\\"\n";

        var catalog = PoCatalog.Parse(po);

        Assert.True(catalog.TryGet("Say \"hi\"\nnow\\", out var value));
        Assert.Equal("Скажи «hi»\nсейчас\\", value);
    }

    [Fact]
    public void SkipsFuzzyAndEmptyTranslations()
    {
        const string po = "#, fuzzy\nmsgid \"Fuzzy\"\nmsgstr \"Нечёткий\"\n\n" +
                          "msgid \"Empty\"\nmsgstr \"\"\n\n" +
                          "msgid \"Ok\"\nmsgstr \"ОК\"\n";

        var catalog = PoCatalog.Parse(po);

        Assert.False(catalog.TryGet("Fuzzy", out _));
        Assert.False(catalog.TryGet("Empty", out _));
        Assert.True(catalog.TryGet("Ok", out var ok));
        Assert.Equal("ОК", ok);
    }

    [Fact]
    public void EntriesWithoutBlankSeparatorAreKept()
    {
        const string po = "msgid \"A\"\nmsgstr \"А\"\nmsgid \"B\"\nmsgstr \"Б\"\n";

        var catalog = PoCatalog.Parse(po);

        Assert.True(catalog.TryGet("A", out var a));
        Assert.Equal("А", a);
        Assert.True(catalog.TryGet("B", out var b));
        Assert.Equal("Б", b);
    }

    [Fact]
    public void EmbeddedRussianCatalogCoversMenuStrings()
    {
        using var stream = typeof(PoCatalog).Assembly.GetManifestResourceStream("CoopLang.main.ru.po");
        Assert.NotNull(stream);

        using var reader = new StreamReader(stream!);
        var catalog = PoCatalog.Parse(reader.ReadToEnd());

        foreach (var key in new[] { "Co-op", "Create lobby", "Join a friend", "Lobby", "New run", "Mod language", "As in game", "Ready: yes" })
        {
            Assert.True(catalog.TryGet(key, out var value), key);
            Assert.NotEqual(key, value);
        }

        Assert.True(catalog.TryGet("Join {0}'s lobby", out var format));
        Assert.Contains("{0}", format);
    }
}

/*
 * ═══════════════════════════════════════
 *  PO CATALOG TESTS — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Проверяют разбор .po (многострочные записи, экранирование, fuzzy/пустые) и что вшитый
 *  в dll русский каталог содержит строки меню кооператива с сохранёнными плейсхолдерами.
 * ═══════════════════════════════════════
*/
