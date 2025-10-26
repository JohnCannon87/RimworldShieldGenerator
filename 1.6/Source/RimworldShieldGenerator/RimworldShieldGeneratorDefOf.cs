using RimWorld;
using Verse;

namespace RimworldShieldGenerator
{
    [DefOf]
    public static class RimworldShieldGeneratorDefOf
    {
        public static SoundDef Laser_Fire;

        static RimworldShieldGeneratorDefOf()
        {
            DefOfHelper.EnsureInitializedInCtor(typeof(RimworldShieldGeneratorDefOf));
        }
    }
}
