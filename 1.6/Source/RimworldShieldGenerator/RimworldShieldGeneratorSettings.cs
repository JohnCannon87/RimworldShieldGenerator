using Verse;

namespace RimworldShieldGenerator
{
    public class RimworldShieldGeneratorSettings : ModSettings
    {
        // Weapon tunables
        public int shieldPowerPerCell = 20;

        public bool enableDebugLogging = false;

        public override void ExposeData()
        {
            Scribe_Values.Look(ref shieldPowerPerCell, "shieldPowerPerCell", 20);
            Scribe_Values.Look(ref enableDebugLogging, "enableDebugLogging", false);
        }
    }
}
