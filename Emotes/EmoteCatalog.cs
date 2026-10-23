namespace DeadCellsMultiplayerMod.Emotes;

internal enum EmotePlayback
{
    Once,
    Loop,
    Hold
}

internal sealed class EmoteDef
{
    public EmoteDef(int id, string label, EmotePlayback playback, string[] icon, params string[] anims)
    {
        Id = id;
        Label = label;
        Playback = playback;
        Icon = icon;
        Anims = anims;
    }

    public int Id { get; }
    public string Label { get; }
    public EmotePlayback Playback { get; }
    public string[] Icon { get; }
    public string[] Anims { get; }

    public bool UsesAnim(string anim)
    {
        foreach (var name in Anims)
        {
            if (string.Equals(name, anim, StringComparison.Ordinal))
                return true;
        }
        return false;
    }
}

internal static class EmoteCatalog
{
    public static readonly EmoteDef[] All =
    [
        new(0, "Hello", EmotePlayback.Once, EmoteIcons.Hello, "hey"),
        new(1, "Yes", EmotePlayback.Once, EmoteIcons.Yes, "yes"),
        new(2, "No", EmotePlayback.Once, EmoteIcons.No, "no"),
        new(3, "What?!", EmotePlayback.Once, EmoteIcons.What, "what"),
        new(4, "Sit", EmotePlayback.Hold, EmoteIcons.Sit, "sit"),
        new(5, "Dance", EmotePlayback.Loop, EmoteIcons.Dance, "travolta", "travoltaIdle"),
        new(6, "Mock", EmotePlayback.Once, EmoteIcons.Mock, "mockery"),
        new(7, "Get lost", EmotePlayback.Once, EmoteIcons.GetLost, "fuckOff")
    ];

    public static EmoteDef? Get(int id) => (uint)id < (uint)All.Length ? All[id] : null;
}

/*
 * ═══════════════════════════════════════
 *  EMOTE CATALOG — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Индекс в All = id эмоции в сети = сектор колеса (0 сверху, дальше по часовой).
 *  Anims — группы атласа героя по порядку: Once играется один раз, Loop — первая
 *  разово, последняя по кругу, Hold — замирает на последнем кадре.
 *  Label — ключ i18n (DYNAMIC_KEYS в tools/i18n.py).
 * ═══════════════════════════════════════
*/
