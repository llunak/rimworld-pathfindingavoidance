using Verse;
using RimWorld;

namespace PathfindingAvoidance;

// Specifies which kind of path costs should be applied.
public enum PathType
{
    None, // Not any of the types below.
    Colony, // Colony pawns (except for animals and exclusions).
    Friendly, // Visiting caravans, etc.
    COUNT
}

public static class PathTypeUtils
{
    public static bool IsEnabled(this PathType pathType)
    {
        return PathfindingAvoidanceMod.settings.IsEnabled( pathType );
    }

    public static PathType GetPathType( PathRequest request )
    {
        // TODO Does this need optimizing?
        // TODO Save last pawn/tick and cache result (this gets called several times in a row).
        // This doesn't actually seem to be called that often.
        Pawn pawn = request.pawn;
        if( pawn == null )
            return PathType.None;
        if( pawn.IsPlayerControlled || Utility.ShouldAlsoTreatAsColonist( pawn ))
        {
            // Player-controlled pawns (colonists, mechs) generally follow the rules,
            // with some exceptions.
            if( pawn.IsAnimal || pawn.Drafted || pawn.Crawling )
                return PathType.None;
            // Firefighting, emergency tending, etc.
            if( pawn.CurJob?.workGiverDef?.emergency == true )
                return PathType.None;
            // Some things inspired by GatheringsUtility.ShouldGuestKeepAttendingGathering().
            if( pawn.health.hediffSet.BleedRateTotal > 0.3f || pawn.health.hediffSet.InLabor())
                return PathType.None;
            // Carrying another downed pawn (but not a baby).
            if( pawn.carryTracker != null && pawn.carryTracker.CarriedThing is Pawn otherPawn
                && otherPawn.Downed && !otherPawn.DevelopmentalStage.Baby())
            {
                    return PathType.None;
            }
            if( pawn.InMentalState )
                return PathType.None;
            return PathType.Colony;
        }
        // Raiders never follow rules.
        Faction mapFaction = request.map.ParentFaction ?? null;
        if( pawn.Faction != mapFaction && pawn.Faction != null && pawn.Faction.HostileTo( mapFaction ))
            return PathType.None;
        // Neutrals follow rules if not in mental state or in fight.
        if( pawn.InMentalState
            || pawn.mindState?.meleeThreat != null
            || pawn.mindState?.enemyTarget != null
            /*|| ( pawn.mindState?.WasRecentlyCombatantTicks( 10 ) ?? false ) does not work unfortunately*/)
        {
            return PathType.None;
        }
        if( pawn.IsAnimal && pawn.Faction == null )
            return PathType.None; // Wild animals ignore rules (animals from friendly factions obey them).
        return PathType.Friendly;
    }
}
