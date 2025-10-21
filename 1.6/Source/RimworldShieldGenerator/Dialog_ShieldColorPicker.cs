// Dialog_ShieldColorPicker.cs
using RimWorld;
using System;
using System.Collections.Generic;
using UnityEngine;
using Verse;

public class Dialog_ShieldColorPicker : Window
{
    private readonly Action<Color> onAccept;

    private Color color;
    private Color oldColor;

    private float alpha;          // 0..1 (controls your inner glow strength)
    private string alphaBuffer;   // numeric text field buffer

    private bool hsvDragging;
    private static Texture2D checkerTex;

    public override Vector2 InitialSize => new Vector2(640f, 500f);
    private static readonly Vector2 ButSize = new Vector2(150f, 38f);

    // Palette like vanilla
    private static readonly List<Color> palette = new List<Color>
    {
        new Color(0.20f, 0.45f, 0.85f, 1f),
        new Color(0.00f, 0.65f, 0.95f, 1f),
        new Color(0.35f, 0.85f, 0.55f, 1f),
        new Color(0.95f, 0.75f, 0.20f, 1f),
        new Color(0.95f, 0.40f, 0.20f, 1f),
        new Color(0.90f, 0.25f, 0.25f, 1f),
        new Color(0.70f, 0.60f, 0.95f, 1f),
        new Color(0.85f, 0.85f, 0.85f, 1f),
        new Color(0.40f, 0.65f, 0.80f, 1f),
    };

    public Dialog_ShieldColorPicker(Color initialColor, Action<Color> onAccept)
    {
        this.onAccept = onAccept ?? (_ => { });

        color = initialColor;
        oldColor = initialColor;
        alpha = Mathf.Clamp01(initialColor.a);
        alphaBuffer = Mathf.RoundToInt(alpha * 100f).ToString();

        forcePause = true;
        absorbInputAroundWindow = true;
        closeOnClickedOutside = true;
        closeOnAccept = false;
    }

    public override void DoWindowContents(Rect inRect)
    {
        // --- Title ---
        Text.Font = GameFont.Medium;
        string title = "Choose a color".CapitalizeFirst();
        Widgets.Label(new Rect(inRect.x, inRect.y, inRect.width, 40f), title);
        Text.Font = GameFont.Small;

        // Working area (minus title + buttons)
        Rect workArea = new Rect(inRect.x, inRect.y + 45f, inRect.width, inRect.height - 95f);

        // --- Layout constants ---
        float leftWidth = 160f;   // RGB/HSV column
        float padding = 20f;
        float wheelSize = 220f;

        // --- Left side: RGB + HSV fields ---
        Rect leftRect = new Rect(workArea.x, workArea.y, leftWidth, workArea.height);
        DrawColorTextfields(leftRect);

        // --- Right side: wheel + palette + previews ---
        Rect rightRect = new Rect(leftRect.xMax + padding, workArea.y, workArea.width - leftWidth - padding, workArea.height);

        // Palette at top
        Rect paletteRect = new Rect(rightRect.x, rightRect.y, rightRect.width, 28f);
        DrawPalette(paletteRect);

        // Wheel centered under palette
        Rect wheelRect = new Rect(
            rightRect.x + (rightRect.width - wheelSize) / 2f,
            paletteRect.yMax + 8f,
            wheelSize,
            wheelSize
        );
        Widgets.HSVColorWheel(wheelRect, ref color, ref hsvDragging, null);

        // Current + old color previews
        Rect previewRect = new Rect(rightRect.x, wheelRect.yMax + 8f, rightRect.width, 48f);
        DrawReadback(previewRect);

        // --- Alpha section ---
        float alphaY = inRect.yMax - 85f;
        float alphaH = 22f;

        string labelText = "Inner Transparency:";
        float labelW = Text.CalcSize(labelText).x + 8f;
        Rect alphaLabel = new Rect(inRect.x + 10f, alphaY, labelW, alphaH);
        Widgets.Label(alphaLabel, labelText);

        Rect sliderRect = new Rect(alphaLabel.xMax + 5f, alphaY, inRect.width - 200f, alphaH);
        float newAlpha = Widgets.HorizontalSlider(sliderRect, alpha, 0f, 1f, false, $"{alpha:P0}", "0%", "100%");
        Rect boxRect = new Rect(sliderRect.xMax + 5f, alphaY, 50f, alphaH);
        Widgets.TextFieldNumeric(boxRect, ref newAlpha, ref alphaBuffer, 0f, 1f);

        if (!Mathf.Approximately(newAlpha, alpha))
        {
            alpha = Mathf.Clamp01(newAlpha);
            color.a = alpha;
        }

        // Alpha preview bar below slider
        Rect alphaPreview = new Rect(inRect.x + 10f, sliderRect.yMax + 6f, inRect.width - 20f, 16f);
        DrawAlphaPreview(alphaPreview, color);

        // --- Buttons ---
        float buttonY = inRect.yMax - ButSize.y;
        Rect cancelRect = new Rect(inRect.x, buttonY, ButSize.x, ButSize.y);
        Rect acceptRect = new Rect(inRect.xMax - ButSize.x, buttonY, ButSize.x, ButSize.y);

        if (Widgets.ButtonText(cancelRect, "Cancel".Translate())) Close();
        if (Widgets.ButtonText(acceptRect, "Accept".Translate()))
        {
            onAccept(color);
            Close();
        }
    }

    private void DrawColorTextfields(Rect rect)
    {
        // RimWorld helper manages both RGB & HSV fields with sync
        var aggregator = new RectAggregator(new Rect(rect.position, new Vector2(rect.width, 0f)), 0);
        // Buffers used by the helper; keep across frames to maintain focus stability
        if (_buffers == null) _buffers = new string[6];
        Widgets.ColorTextfields(ref aggregator, ref color, ref _buffers, ref _bufferColor, null, "shieldColorText", Widgets.ColorComponents.All, Widgets.ColorComponents.All);
    }
    private string[] _buffers;
    private Color _bufferColor;

    private void DrawPalette(Rect rect)
    {
        float height;
        Widgets.ColorSelector(rect, ref color, palette, out height);
    }

    private void DrawReadback(Rect rect)
    {
        // Left: current color; Right: old color
        rect.SplitVertically(rect.width * 0.5f, out var left, out var right);

        using (new TextBlock(TextAnchor.MiddleLeft))
        {
            var labelW = Mathf.Max(100f, "CurrentColor".Translate().CapitalizeFirst().GetWidthCached());
            var rowH = 22f;

            // Current
            Rect l0 = new Rect(left.x, left.y, left.width, rowH);
            Widgets.Label(new Rect(l0.x, l0.y, labelW, l0.height), "CurrentColor".Translate().CapitalizeFirst());
            Widgets.DrawBoxSolid(new Rect(l0.x + labelW + 6f, l0.y, left.width - labelW - 6f, l0.height), color);

            // Old
            Rect l1 = new Rect(left.x, left.y + rowH + 4f, left.width, rowH);
            Widgets.Label(new Rect(l1.x, l1.y, labelW, l1.height), "OldColor".Translate().CapitalizeFirst());
            Widgets.DrawBoxSolid(new Rect(l1.x + labelW + 6f, l1.y, left.width - labelW - 6f, l1.height), oldColor);
        }
    }

    private void DrawAlphaPreview(Rect rect, Color c)
    {
        // checkerboard
        GUI.color = Color.white;
        GUI.DrawTextureWithTexCoords(rect, CheckerTex, new Rect(0f, 0f, rect.width / 16f, rect.height / 16f));
        // solid overlay with chosen alpha
        var tex = SolidColorMaterials.NewSolidColorTexture(new Color(c.r, c.g, c.b, c.a));
        GUI.DrawTexture(rect, tex);
        Widgets.DrawBox(rect);
    }

    private static Texture2D CheckerTex
    {
        get
        {
            if (checkerTex != null) return checkerTex;
            const int s = 8;
            checkerTex = new Texture2D(s * 2, s * 2);
            var a = new Color(0.65f, 0.65f, 0.65f, 1f);
            var b = new Color(0.85f, 0.85f, 0.85f, 1f);
            for (int y = 0; y < s * 2; y++)
                for (int x = 0; x < s * 2; x++)
                    checkerTex.SetPixel(x, y, ((x < s) ^ (y < s)) ? a : b);
            checkerTex.Apply();
            checkerTex.wrapMode = TextureWrapMode.Repeat;
            return checkerTex;
        }
    }
}
