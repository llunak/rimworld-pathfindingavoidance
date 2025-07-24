using Verse;

namespace PathfindingAvoidance;

public class Utility
{
    // A hook allowing mods to add additional pawn types
    // to be treat the same way as colonists.
    public static bool ShouldAlsoTreatAsColonist( Pawn pawn )
    {
        return false;
    }
}
