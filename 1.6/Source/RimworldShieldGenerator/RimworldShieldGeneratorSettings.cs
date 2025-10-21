using Verse;

namespace RimworldShieldGenerator
{
    public class RimworldShieldGeneratorSettings : ModSettings
    {
        public bool enableDebugLogging = false;
        public float shieldInterceptCostWd = 1;
        public int percentageOfDamageDrained = 50;
        public int shieldIdleWatts = 500;
        public int shieldCooldownTicks = 5000;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref shieldInterceptCostWd, "shieldInterceptCostWd", 1);
            Scribe_Values.Look(ref percentageOfDamageDrained, "percentageOfDamageDrained", 50);
            Scribe_Values.Look(ref shieldIdleWatts, "shieldIdleWatts", 500);
            Scribe_Values.Look(ref shieldCooldownTicks, "shieldCooldownTicks", 5000);
            Scribe_Values.Look(ref enableDebugLogging, "enableDebugLogging", false);
        }
    }
}
