using dc.h2d;
using HaxeProxy.Runtime;

namespace DeadCellsMultiplayerMod.Emotes;

internal static class EmoteIcons
{
    public const int Size = 9;
    public const int OutlineColor = 0x081018;

    public static readonly string[] Hello =
    [
        ".#.#.#...",
        ".#.#.#.#.",
        ".#.#.#.#.",
        ".#######.",
        "########.",
        "########.",
        ".#######.",
        "..#####..",
        "...###..."
    ];

    public static readonly string[] Yes =
    [
        "........g",
        ".......gg",
        "......ggg",
        "g....ggg.",
        "gg..ggg..",
        "gggggg...",
        ".gggg....",
        "..gg.....",
        "........."
    ];

    public static readonly string[] No =
    [
        "oo.....oo",
        "ooo...ooo",
        ".ooo.ooo.",
        "..ooooo..",
        "...ooo...",
        "..ooooo..",
        ".ooo.ooo.",
        "ooo...ooo",
        "oo.....oo"
    ];

    public static readonly string[] What =
    [
        ".###..yy.",
        "##.##.yy.",
        "...##.yy.",
        "..##..yy.",
        ".##...yy.",
        ".##......",
        ".........",
        ".##...yy.",
        ".##...yy."
    ];

    public static readonly string[] Sit =
    [
        ".##......",
        ".##......",
        ".##......",
        ".##......",
        ".#######.",
        ".#######.",
        ".##...##.",
        ".##...##.",
        ".##...##."
    ];

    public static readonly string[] Dance =
    [
        "...yyyyyy",
        "...yyyyyy",
        "...y....y",
        "...y....y",
        "...y....y",
        ".yyy..yyy",
        "yyyy.yyyy",
        "yyyy.yyyy",
        ".yy...yy."
    ];

    public static readonly string[] Mock =
    [
        "..#####..",
        ".#######.",
        "##.###.##",
        "##.###.##",
        "#########",
        "#.......#",
        "##..oo.##",
        ".###oo##.",
        "..#####.."
    ];

    public static readonly string[] GetLost =
    [
        "..ooooo..",
        ".ooooooo.",
        "o.ooooo.o",
        "oo.ooo.oo",
        "o..ooo..o",
        "ooooooooo",
        "oo.....oo",
        ".o.ooo.o.",
        "..ooooo.."
    ];

    readonly record struct Run(int Color, int X, int Y, int Width);

    static readonly Dictionary<string[], Run[]> _runs = new(ReferenceEqualityComparer.Instance);

    public static void Draw(Graphics g, string[] map, double centerX, double centerY, double pixel, double alpha)
    {
        if (g == null || map == null || pixel <= 0.0)
            return;

        var runs = GetRuns(map);
        var origin = (Size + 2) * pixel * 0.5;
        var left = centerX - origin;
        var top = centerY - origin;
        var currentColor = int.MinValue;

        foreach (var run in runs)
        {
            if (run.Color != currentColor)
            {
                if (currentColor != int.MinValue)
                    g.endFill();
                var color = run.Color;
                var a = alpha;
                g.beginFill(Ref<int>.From(ref color), Ref<double>.From(ref a));
                currentColor = run.Color;
            }
            g.drawRect(left + run.X * pixel, top + run.Y * pixel, run.Width * pixel, pixel);
        }

        if (currentColor != int.MinValue)
            g.endFill();
    }

    static Run[] GetRuns(string[] map)
    {
        if (_runs.TryGetValue(map, out var cached))
            return cached;

        const int grid = Size + 2;
        var cells = new int[grid, grid];
        for (var y = 0; y < grid; y++)
        for (var x = 0; x < grid; x++)
            cells[y, x] = -1;

        for (var y = 0; y < Size && y < map.Length; y++)
        {
            var row = map[y];
            for (var x = 0; x < Size && x < row.Length; x++)
                cells[y + 1, x + 1] = ColorOf(row[x]);
        }

        var outline = new bool[grid, grid];
        for (var y = 0; y < grid; y++)
        for (var x = 0; x < grid; x++)
        {
            if (cells[y, x] >= 0)
                continue;
            for (var oy = -1; oy <= 1 && !outline[y, x]; oy++)
            for (var ox = -1; ox <= 1; ox++)
            {
                var ny = y + oy;
                var nx = x + ox;
                if (ny < 0 || nx < 0 || ny >= grid || nx >= grid || cells[ny, nx] < 0)
                    continue;
                outline[y, x] = true;
                break;
            }
        }

        var runs = new List<Run>();
        CollectRuns(runs, grid, (x, y) => outline[y, x] ? OutlineColor : -1);
        var colors = new SortedSet<int>();
        foreach (var c in cells)
        {
            if (c >= 0)
                colors.Add(c);
        }
        foreach (var color in colors)
            CollectRuns(runs, grid, (x, y) => cells[y, x] == color ? color : -1);

        var result = runs.ToArray();
        _runs[map] = result;
        return result;
    }

    static void CollectRuns(List<Run> runs, int grid, Func<int, int, int> colorAt)
    {
        for (var y = 0; y < grid; y++)
        {
            var x = 0;
            while (x < grid)
            {
                var color = colorAt(x, y);
                if (color < 0)
                {
                    x++;
                    continue;
                }
                var start = x;
                while (x < grid && colorAt(x, y) == color)
                    x++;
                runs.Add(new Run(color, start, y, x - start));
            }
        }
    }

    static int ColorOf(char c) => c switch
    {
        '#' => 0xf4f1e8,
        'o' => 0xe0484f,
        'y' => 0xf0b13c,
        'g' => 0x7ed957,
        _ => -1
    };
}

/*
 * ═══════════════════════════════════════
 *  EMOTE ICONS — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Иконка — карта 9×9: '#' крем, 'o' красный, 'y' золото, 'g' зелёный, '.' пусто.
 *  Обводка 1px 0x081018 — дилатация по 8 соседям, сетка 11×11 с центром в (centerX, centerY).
 *  Карта один раз сворачивается в горизонтальные отрезки по цветам (кэш по ссылке),
 *  рисуются drawRect: один beginFill на цвет, сначала обводка, потом заливка.
 * ═══════════════════════════════════════
*/
