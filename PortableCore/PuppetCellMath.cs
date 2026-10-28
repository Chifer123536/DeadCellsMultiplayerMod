namespace DeadCellsMultiplayerMod.PortableCore;

internal static class PuppetCellMath
{
    public const double CellPx = 24.0;

    public static void ToCellX(double px, out int cell, out double frac)
    {
        var c = px / CellPx;
        cell = (int)Math.Floor(c);
        frac = Math.Clamp(c - cell, 0.0, 1.0);
    }

    public static void ToCellFeetY(double px, out int cell, out double frac)
    {
        var c = px / CellPx;
        cell = (int)Math.Ceiling(c) - 1;
        frac = Math.Clamp(c - cell, 1e-6, 1.0);
    }
}

/*
 * ═══════════════════════════════════════
 *  PUPPET CELL MATH — КАК РАБОТАЕТ
 * ═══════════════════════════════════════
 *  Пиксели → клетка (24 px) + доля для setPosCase.
 *  X: floor, доля [0, 1).
 *  Y (ноги): ceil − 1, доля (0, 1]. Стоящая сущность в игре хранится как yr = 1 в клетке над полом;
 *  floor от y на границе клетки (как в setPosPixel) дал бы клетку пола с yr = 0 — моб в тайлах.
 * ═══════════════════════════════════════
*/
