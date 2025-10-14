using RimWorld;
using Verse;

namespace RimworldShieldGenerator
{
    [StaticConstructorOnStartup]
    public static class RimworldShieldGenerator_Initializer
    {
        static RimworldShieldGenerator_Initializer()
        {
            LongEventHandler.ExecuteWhenFinished(ApplySettings);
        }

        public static void ApplySettings()
        {
            var settings = RimworldShieldGeneratorMod.settings;

            Log.Message("[ShieldGenerator] Custom parameters applied.");
        }
    }
}
