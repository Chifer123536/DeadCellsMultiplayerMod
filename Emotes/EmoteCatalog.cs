namespace DeadCellsMultiplayerMod.Emotes;

internal readonly record struct EmoteStep(string Group, int Plays, bool Reverse)
{
    public const int LoopForever = 0;
    public const int HoldLastFrame = -1;

    public static EmoteStep Once(string group) => new(group, 1, false);
    public static EmoteStep Reversed(string group) => new(group, 1, true);
    public static EmoteStep Times(string group, int plays) => new(group, plays, false);
    public static EmoteStep Loop(string group) => new(group, LoopForever, false);
    public static EmoteStep Hold(string group) => new(group, HoldLastFrame, false);
}

internal sealed class EmoteDef
{
    public EmoteDef(int id, string label, string[] icon, params EmoteStep[] steps)
    {
        Id = id;
        Label = label;
        Icon = icon;
        Steps = steps;
    }

    public int Id { get; }
    public string Label { get; }
    public string[] Icon { get; }
    public EmoteStep[] Steps { get; }

    public bool EndsOnItsOwn => Steps.Length > 0 && Steps[^1].Plays > 0;

    public bool UsesAnim(string anim)
    {
        foreach (var step in Steps)
        {
            if (string.Equals(step.Group, anim, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

internal static class EmoteCatalog
{
    public static readonly EmoteDef[] All =
    [
        new(0, "Hello", EmoteIcons.Hello, EmoteStep.Reversed("heyIdle"), EmoteStep.Loop("hey")),
        new(1, "Yes", EmoteIcons.Yes, EmoteStep.Once("yes")),
        new(2, "No", EmoteIcons.No, EmoteStep.Once("no")),
        new(3, "What?!", EmoteIcons.What,
            EmoteStep.Once("what"), EmoteStep.Once("idleThink"), EmoteStep.Times("think", 2), EmoteStep.Reversed("idleThink")),
        new(4, "Sit", EmoteIcons.Sit, EmoteStep.Hold("sit")),
        new(5, "Dance", EmoteIcons.Dance, EmoteStep.Loop("travolta")),
        new(6, "Mock", EmoteIcons.Mock, EmoteStep.Once("mockery")),
        new(7, "Get lost", EmoteIcons.GetLost, EmoteStep.Once("fuckOff"))
    ];

    public static EmoteDef? Get(int id) => (uint)id < (uint)All.Length ? All[id] : null;
}

/*
 * ═══════════════════════════════════════
 *  EMOTE CATALOG — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Индекс в All = id эмоции в сети = сектор колеса (0 сверху, дальше по часовой).
 *  Steps — цепочка групп атласа героя, первая играется сразу, остальные в очередь AnimManager.
 *  Plays > 0 — столько проходов; LoopForever — по кругу до отмены; HoldLastFrame — стоп-кадр
 *  (pauseOnLastFrame) до отмены. Reverse — задом наперёд: так XIdle (переход X → idle)
 *  становится входом в позу, как в катсценах игры (heyIdle reverse → hey loop).
 *  EndsOnItsOwn — последний шаг конечный, эмоция закончится сама и герой вернётся к стейт-анимациям.
 *  Label — ключ i18n (DYNAMIC_KEYS в tools/i18n.py).
 * ═══════════════════════════════════════
*/
