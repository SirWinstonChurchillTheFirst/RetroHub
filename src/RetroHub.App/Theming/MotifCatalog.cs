using Avalonia;
using Avalonia.Media;

namespace RetroHub.App.Theming;

/// <summary>
/// Category motifs (DESIGN 6.2) on a 120×120 grid, standing on y = 120. Preliminary shapes (M1/M2); the final
/// drawings and the outsider test follow in M5. Geometries are parsed once and shared.
/// </summary>
public static class MotifCatalog
{
    /// <summary>Body (filled in the system's "Zweitfarbe" or "Farbe") and detail (label/screen) of a motif.</summary>
    public sealed record Shape(Geometry Body, Geometry? Detail, bool BodyInColor);

    private static readonly Dictionary<Motif, Shape> Cache = new();

    public static Shape Get(Motif motif)
    {
        lock (Cache)
        {
            if (Cache.TryGetValue(motif, out var s)) return s;
            s = Create(motif);
            Cache[motif] = s;
            return s;
        }
    }

    private static Geometry P(string data) => Geometry.Parse(data);

    private static Geometry Disc(double cx, double cy, double r, double hole)
    {
        var g = new GeometryGroup { FillRule = FillRule.EvenOdd };
        g.Children.Add(new EllipseGeometry(new Rect(cx - r, cy - r, 2 * r, 2 * r)));
        g.Children.Add(new EllipseGeometry(new Rect(cx - hole, cy - hole, 2 * hole, 2 * hole)));
        return g;
    }

    private static Geometry Group(params Geometry[] parts)
    {
        var g = new GeometryGroup { FillRule = FillRule.NonZero };
        foreach (var p in parts) g.Children.Add(p);
        return g;
    }

    private static Shape Create(Motif motif) => motif switch
    {
        Motif.Cart8 => new(P("M14,10 H106 V120 H14 Z"), P("M24,46 H96 V112 H24 Z"), false),
        Motif.Cart16 => new(P("M18,22 Q18,8 32,8 H88 Q102,8 102,22 V120 H18 Z"), P("M30,34 H90 V100 H30 Z"), false),
        Motif.Cart64 => new(P("M14,30 L26,10 H94 L106,30 V120 H14 Z"), P("M28,38 H92 V104 H28 Z"), false),
        Motif.CartStack => new(P("M30,20 Q30,8 42,8 H78 Q90,8 90,20 V42 H30 Z M18,62 Q18,50 30,50 H90 Q102,50 102,62 V120 H18 Z"),
            P("M40,66 H80 V108 H40 Z"), false),
        Motif.Card => new(P("M20,60 H100 V120 H20 Z"), P("M30,108 H34 V114 H30 Z M42,108 H46 V114 H42 Z M54,108 H58 V114 H54 Z M66,108 H70 V114 H66 Z M78,108 H82 V114 H78 Z"), false),
        Motif.HandheldTall or Motif.HandheldTallDouble =>
            new(P("M30,4 H90 L98,12 V120 H22 V12 Z"), P("M34,16 H86 V62 H34 Z"), true),
        Motif.HandheldWide => new(P("M14,40 H106 L114,48 V112 L106,120 H14 L6,112 V48 Z"), P("M38,52 H82 V100 H38 Z"), true),
        Motif.HandheldBroad => new(P("M12,64 H108 L116,72 V112 L108,120 H12 L4,112 V72 Z"), P("M36,72 H84 V112 H36 Z"), true),
        Motif.Clamshell => new(P("M18,10 H102 V56 H18 Z M18,64 H102 V120 H18 Z M14,58 H106 V62 H14 Z"), P("M28,18 H92 V50 H28 Z"), true),
        Motif.Disc or Motif.DiscTrack or Motif.DiscBands or Motif.DiscHalftone => new(Disc(60, 68, 52, 9), null, true),
        Motif.MiniDisc => new(Disc(60, 82, 38, 13), null, true),
        Motif.Tray => new(Group(Disc(60, 70, 40, 7), P("M8,86 H112 V120 H8 Z")), P("M48,100 H72 V104 H48 Z"), true),
        Motif.Stand => new(Group(Disc(60, 36, 28, 6), P("M44,30 H76 V120 H44 Z")), P("M50,30 H70 V34 H50 Z"), true),
        Motif.Tablet => new(P("M24,30 H96 V104 H24 Z"), P("M34,40 H86 V94 H34 Z"), true),
        Motif.Dock => new(P("M10,68 H34 V96 H86 V68 H110 V120 H10 Z M26,18 H94 V96 H26 Z"), P("M32,24 H88 V80 H32 Z"), true),
        Motif.TwinLens => new(P("M14,40 H106 V84 H14 Z M36,112 H84 V120 H36 Z M56,84 H64 V112 H56 Z"), null, true),
        Motif.ControlPanel => new(Group(P("M8,72 H112 L104,120 H16 Z M37,44 H43 V72 H37 Z"), new EllipseGeometry(new Rect(31, 29, 18, 18))), null, true),
        Motif.Stick => new(Group(P("M24,92 H96 V120 H24 Z M57,40 H63 V92 H57 Z"), new EllipseGeometry(new Rect(50, 26, 20, 20))), null, true),
        Motif.Floppy => new(P("M14,8 H96 L110,22 V120 H14 Z"), P("M28,64 H96 V120 H28 Z"), false),
        Motif.KeyBlock => new(KeyBlock(), null, true),
        Motif.Tower => new(P("M30,6 H90 V120 H30 Z"), P("M38,20 H82 V22 H38 Z M38,36 H82 V38 H38 Z M38,52 H82 V54 H38 Z M38,68 H82 V70 H38 Z M38,84 H82 V86 H38 Z M38,100 H82 V102 H38 Z"), false),
        _ => new(P("M14,10 H106 V120 H14 Z"), null, false),
    };

    private static Geometry KeyBlock()
    {
        var g = new GeometryGroup();
        for (var row = 0; row < 3; row++)
            for (var col = 0; col < 5; col++)
            {
                double x = 12 + col * 20, y = 62 + row * 20;
                g.Children.Add(P($"M{x + 3},{y} H{x + 11} L{x + 14},{y + 3} V{y + 11} L{x + 11},{y + 14} H{x + 3} L{x},{y + 11} V{y + 3} Z"));
            }
        return g;
    }

    /// <summary>Draws the motif of <paramref name="look"/> standing on the bottom edge of <paramref name="box"/> (a square area).</summary>
    public static void Draw(DrawingContext ctx, SystemLook look, Rect box, double opacity = 1, bool silhouette = false)
    {
        var shape = Get(look.Motif);
        var scale = Math.Min(box.Width, box.Height) / 120.0;
        using (ctx.PushOpacity(opacity))
        using (ctx.PushTransform(Matrix.CreateScale(scale, scale) * Matrix.CreateTranslation(box.X + (box.Width - 120 * scale) / 2, box.Bottom - 120 * scale)))
        {
            if (silhouette)
            {
                ctx.DrawGeometry(look.ColorBrush, null, shape.Body);
                return;
            }
            ctx.DrawGeometry(shape.BodyInColor ? look.ColorBrush : look.SecondBrush, null, shape.Body);
            if (shape.Detail != null) ctx.DrawGeometry(shape.BodyInColor ? look.WallBrush : look.ColorBrush, null, shape.Detail);
        }
    }
}
