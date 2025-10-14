using RimWorld;
using Verse;

namespace RimworldShieldGenerator
{
    [DefOf]
    public static class RimworldShieldGeneratorDefOf
    {
        public static ThingDef Mote_LaserGraserBeamBase;
        public static SoundDef Laser_Fire;

        static RimworldShieldGeneratorDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RimworldShieldGeneratorDefOf));
        }
    }
}
