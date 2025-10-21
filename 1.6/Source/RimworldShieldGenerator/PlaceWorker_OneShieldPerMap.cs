using Verse;
using RimWorld;

namespace RimworldShieldGenerator
{
    public class PlaceWorker_OneShieldPerMap : PlaceWorker
    {
        public override AcceptanceReport AllowsPlacing(
            BuildableDef checkingDef,
            IntVec3 loc,
            Rot4 rot,
            Map map,
            Thing thingToIgnore = null,
            Thing thing = null)
        {
            // Only care about things that are actual buildings
            if (checkingDef is ThingDef def)
            {
                bool exists = map.listerThings.ThingsOfDef(def).Any(t => t.Spawned);
                if (exists)
                    return new AcceptanceReport("Only one shield generator can be built per map.");
            }

            return true;
        }
    }
}
