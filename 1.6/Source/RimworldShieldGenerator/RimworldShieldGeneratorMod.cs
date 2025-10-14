using System;
using UnityEngine;
using Verse;

namespace RimworldShieldGenerator
{
    public class RimworldShieldGeneratorMod : Mod
    {
        public static RimworldShieldGeneratorSettings settings;
        private Vector2 scrollPosition; // <-- for scroll tracking
        private float scrollViewHeight; // <-- tracks total height of content

        public RimworldShieldGeneratorMod(ModContentPack pack) : base(pack)
        {
            settings = GetSettings<RimworldShieldGeneratorSettings>();
        }

        public override void DoSettingsWindowContents(Rect inRect)
        {
            // Prepare scroll variables
            float scrollBarWidth = 20f;
            Rect outRect = inRect;
            outRect.width -= scrollBarWidth;

            // Estimate a big enough height for content initially to prevent clipping
            Rect viewRect = new Rect(0f, 0f, outRect.width - 10f, 1200f);

            // Begin scroll view
            Widgets.BeginScrollView(inRect, ref scrollPosition, viewRect);
            Listing_Standard list = new Listing_Standard();
            list.Begin(viewRect);

            // --- General ---
            list.CheckboxLabeled("Enable Debug Logging", ref settings.enableDebugLogging);
            list.Gap(10f);

            DrawIntSetting(list, "Shield Power Per Cell", ref settings.shieldPowerPerCell, 1, 100);
           
            list.End();
            Widgets.EndScrollView();

            // Update for next frame — ensures the viewRect is tall enough
            scrollViewHeight = list.CurHeight + 50f;
        }


        public override string SettingsCategory() => "Shield Generator";

        // --- Helper Methods ---
        private void DrawFloatSetting(Listing_Standard list, string label, ref float value, float min, float max)
        {
            list.Label($"{label}: {value:F2}");
            float newValue = Widgets.HorizontalSlider(list.GetRect(22f), value, min, max);
            value = (float)Math.Round(newValue, 2);

            Rect textRect = list.GetRect(24f);
            string buffer = value.ToString("F2");
            Widgets.TextFieldNumeric(textRect, ref value, ref buffer, min, max);
            list.Gap(6f);
        }

        private void DrawIntSetting(Listing_Standard list, string label, ref int value, int min, int max)
        {
            list.Label($"{label}: {value}");
            Rect textRect = list.GetRect(24f);
            string buffer = value.ToString();
            Widgets.IntEntry(textRect, ref value, ref buffer, 1);
            value = Mathf.Clamp(value, min, max);
            list.Gap(6f);
        }
    }
}
