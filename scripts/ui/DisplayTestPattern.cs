using Godot;

public partial class DisplayTestPattern : Control
{
    public enum PatternType
    {
        ColorBars,
        Grayscale,
        Geometry,
        Crt,
        Gameplay,
    }

    public PatternType Pattern { get; set; }

    public override void _Draw()
    {
        Rect2 bounds = new(Vector2.Zero, Size);
        DrawRect(bounds, new Color("#080a10"));

        switch (Pattern)
        {
            case PatternType.ColorBars:
                drawColorBars();
                break;
            case PatternType.Grayscale:
                drawGrayscale();
                break;
            case PatternType.Geometry:
                drawGeometry();
                break;
            case PatternType.Crt:
                drawCrt();
                break;
            case PatternType.Gameplay:
                drawGameplay();
                break;
        }
    }

    private void drawColorBars()
    {
        Color[] bars =
        [
            Colors.White,
            Colors.Yellow,
            Colors.Cyan,
            Colors.Green,
            Colors.Magenta,
            Colors.Red,
            Colors.Blue,
        ];
        float margin = Size.X * 0.06f;
        float barTop = Size.Y * 0.1f;
        float barHeight = Size.Y * 0.55f;
        float barWidth = (Size.X - margin * 2) / bars.Length;

        for (int i = 0; i < bars.Length; i++)
        {
            DrawRect(new Rect2(margin + i * barWidth, barTop, barWidth + 1, barHeight), bars[i]);
        }

        Color[] references = [Colors.Black, new("#101010"), new("#202020"), new("#404040"), new("#808080"), new("#c0c0c0"), Colors.White];
        float referenceTop = Size.Y * 0.73f;
        float referenceHeight = Size.Y * 0.16f;
        float referenceWidth = (Size.X - margin * 2) / references.Length;
        for (int i = 0; i < references.Length; i++)
        {
            DrawRect(
                new Rect2(margin + i * referenceWidth, referenceTop, referenceWidth + 1, referenceHeight),
                references[i]
            );
        }
    }

    private void drawGrayscale()
    {
        float margin = Size.X * 0.06f;
        float rampTop = Size.Y * 0.16f;
        float rampHeight = Size.Y * 0.3f;
        const int steps = 16;
        float stepWidth = (Size.X - margin * 2) / steps;

        for (int i = 0; i < steps; i++)
        {
            float value = i / (float)(steps - 1);
            DrawRect(new Rect2(margin + i * stepWidth, rampTop, stepWidth + 1, rampHeight), new Color(value, value, value));
        }

        drawLevelPatches(margin, Size.Y * 0.58f, Size.X - margin * 2, Size.Y * 0.26f, false);
    }

    private void drawLevelPatches(float x, float y, float width, float height, bool white)
    {
        Color[] values = white
            ? [new("#ffffff"), new("#f8f8f8"), new("#eeeeee"), new("#dddddd"), new("#c0c0c0")]
            : [new("#000000"), new("#080808"), new("#101010"), new("#202020"), new("#404040")];
        DrawRect(new Rect2(x, y, width, height), white ? Colors.Black : Colors.White);
        float patchWidth = width / values.Length;
        for (int i = 0; i < values.Length; i++)
        {
            DrawRect(
                new Rect2(x + i * patchWidth + patchWidth * 0.1f, y + height * 0.15f, patchWidth * 0.8f, height * 0.7f),
                values[i]
            );
        }
    }

    private void drawGeometry()
    {
        Rect2 bounds = insetRect(0.06f);
        DrawRect(bounds, new Color("#f3f5f7"), false, 2);

        foreach (float inset in new[] { 0.1f, 0.2f })
        {
            DrawRect(insetRect(inset), new Color(0.5f, 0.85f, 1f, 0.8f), false, 1);
        }

        Vector2 center = Size / 2;
        float radius = Mathf.Min(Size.X, Size.Y) * 0.28f;
        DrawArc(center, radius, 0, Mathf.Tau, 128, Colors.White, 2, true);

        for (int i = 1; i < 10; i++)
        {
            float x = Size.X * i / 10f;
            float y = Size.Y * i / 10f;
            DrawLine(new Vector2(x, Size.Y * 0.06f), new Vector2(x, Size.Y * 0.94f), new Color(1, 1, 1, 0.16f));
            DrawLine(new Vector2(Size.X * 0.06f, y), new Vector2(Size.X * 0.94f, y), new Color(1, 1, 1, 0.16f));
        }

        DrawLine(new Vector2(center.X, Size.Y * 0.06f), new Vector2(center.X, Size.Y * 0.94f), Colors.Yellow, 2);
        DrawLine(new Vector2(Size.X * 0.06f, center.Y), new Vector2(Size.X * 0.94f, center.Y), Colors.Yellow, 2);
    }

    private void drawCrt()
    {
        for (int i = 0; i < 3; i++)
        {
            float inset = 0.02f + i * 0.04f;
            Color color = i == 0 ? Colors.White : i == 1 ? Colors.Cyan : Colors.Yellow;
            DrawRect(insetRect(inset), color, false, 2);
        }

        float centerX = Size.X / 2;
        float centerY = Size.Y / 2;
        float stripeStart = Size.X * 0.08f;
        float stripeEnd = Size.X * 0.92f;
        for (float x = stripeStart; x < stripeEnd; x += 2)
        {
            bool bright = ((int)x / 2) % 2 == 0;
            Color color = bright ? new Color(1, 1, 1, 0.8f) : new Color(0, 0, 0, 0.8f);
            DrawLine(new Vector2(x, Size.Y * 0.12f), new Vector2(x, Size.Y * 0.28f), color);
        }

        drawConvergenceMark(new Vector2(Size.X * 0.25f, centerY), new Color(1, 0, 0, 0.65f));
        drawConvergenceMark(new Vector2(Size.X * 0.25f, centerY), new Color(0, 1, 0, 0.65f));
        drawConvergenceMark(new Vector2(Size.X * 0.25f, centerY), new Color(0, 0, 1, 0.65f));
        drawConvergenceMark(new Vector2(Size.X * 0.75f, centerY), new Color(1, 0, 0, 0.65f));
        drawConvergenceMark(new Vector2(Size.X * 0.75f, centerY), new Color(0, 1, 0, 0.65f));
        drawConvergenceMark(new Vector2(Size.X * 0.75f, centerY), new Color(0, 0, 1, 0.65f));

        DrawRect(new Rect2(centerX - 10, centerY + Size.Y * 0.19f, 20, 20), Colors.White);
        DrawRect(new Rect2(centerX - 7, centerY + Size.Y * 0.19f + 3, 14, 14), Colors.Black);
        DrawRect(new Rect2(centerX - 4, centerY + Size.Y * 0.19f + 6, 8, 8), Colors.White);
    }

    private void drawConvergenceMark(Vector2 center, Color color)
    {
        DrawLine(center - Vector2.Left * 12, center + Vector2.Left * 12, color, 2);
        DrawLine(center - Vector2.Up * 12, center + Vector2.Up * 12, color, 2);
    }

    private void drawGameplay()
    {
        DrawRect(new Rect2(Size.X * 0.08f, Size.Y * 0.08f, Size.X * 0.84f, Size.Y * 0.84f), new Color("#101722"));

        Rect2 playfield = new(Size.X * 0.24f, Size.Y * 0.2f, Size.X * 0.52f, Size.Y * 0.62f);
        DrawRect(playfield, new Color("#141d2a"), false, 2);
        for (int i = 1; i < 5; i++)
        {
            float x = playfield.Position.X + playfield.Size.X * i / 5f;
            DrawLine(new Vector2(x, playfield.Position.Y), new Vector2(x, playfield.End.Y), new Color(1, 1, 1, 0.16f));
        }

        DrawRect(new Rect2(Size.X * 0.08f, Size.Y * 0.12f, Size.X * 0.14f, Size.Y * 0.7f), new Color("#18212f"));
        DrawRect(new Rect2(Size.X * 0.78f, Size.Y * 0.12f, Size.X * 0.14f, Size.Y * 0.7f), new Color("#18212f"));
        DrawRect(new Rect2(Size.X * 0.24f, Size.Y * 0.12f, Size.X * 0.52f, Size.Y * 0.025f), new Color("#7d5cff"));
        DrawRect(new Rect2(Size.X * 0.24f, Size.Y * 0.12f, Size.X * 0.34f, Size.Y * 0.025f), new Color("#42d9ff"));

        drawText("SCORE", new Vector2(Size.X * 0.1f, Size.Y * 0.2f), 16, new Color("#aab8ca"));
        drawText("0123456", new Vector2(Size.X * 0.1f, Size.Y * 0.27f), 25, Colors.White);
        drawText("COMBO", new Vector2(Size.X * 0.1f, Size.Y * 0.48f), 16, new Color("#aab8ca"));
        drawText("128", new Vector2(Size.X * 0.1f, Size.Y * 0.55f), 30, new Color("#62e6ff"));
        drawText("ACCURACY", new Vector2(Size.X * 0.79f, Size.Y * 0.2f), 15, new Color("#aab8ca"));
        drawText("98.76%", new Vector2(Size.X * 0.79f, Size.Y * 0.27f), 21, Colors.White);
        drawText("HEALTH", new Vector2(Size.X * 0.79f, Size.Y * 0.48f), 15, new Color("#aab8ca"));
        DrawRect(new Rect2(Size.X * 0.8f, Size.Y * 0.55f, Size.X * 0.1f, Size.Y * 0.025f), new Color("#2f3b4e"));
        DrawRect(new Rect2(Size.X * 0.8f, Size.Y * 0.55f, Size.X * 0.08f, Size.Y * 0.025f), new Color("#53e38b"));

        Vector2[] notes =
        [
            new(Size.X * 0.34f, Size.Y * 0.35f),
            new(Size.X * 0.48f, Size.Y * 0.48f),
            new(Size.X * 0.62f, Size.Y * 0.31f),
        ];
        Color[] noteColors = [new("#ff648c"), new("#5be2ff"), new("#ffd75e")];
        for (int i = 0; i < notes.Length; i++)
        {
            DrawCircle(notes[i], Mathf.Min(Size.X, Size.Y) * 0.025f, noteColors[i]);
        }

        DrawCircle(new Vector2(Size.X * 0.54f, Size.Y * 0.68f), Mathf.Min(Size.X, Size.Y) * 0.035f, Colors.White, false, 3);
        DrawLine(new Vector2(Size.X * 0.51f, Size.Y * 0.68f), new Vector2(Size.X * 0.57f, Size.Y * 0.68f), Colors.White, 2);
        DrawLine(new Vector2(Size.X * 0.54f, Size.Y * 0.65f), new Vector2(Size.X * 0.54f, Size.Y * 0.71f), Colors.White, 2);
    }

    private void drawText(string text, Vector2 position, int fontSize, Color color)
    {
        DrawString(GetThemeDefaultFont(), position, text, HorizontalAlignment.Left, -1, fontSize, color);
    }

    private Rect2 insetRect(float amount)
    {
        Vector2 margin = Size * amount;
        return new Rect2(margin, Size - margin * 2);
    }
}
