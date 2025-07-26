using HarmonyLib;
using Verse;
using System.Reflection;

namespace PathfindingAvoidance;

public class Utility
{
    // A hook allowing mods to add additional pawn types
    // to be treat the same way as colonists.
    public static bool ShouldAlsoTreatAsColonist( Pawn pawn )
    {
        return false;
    }

    // Better Pawn Control emergency mode.
    private delegate bool BPCOnAlertDelegate();
    private static BPCOnAlertDelegate bpcOnAlertDelegate = InitBPCOnAlertDelegate();
    private static BPCOnAlertDelegate InitBPCOnAlertDelegate()
    {
        MethodInfo method = AccessTools.PropertyGetter( "BetterPawnControl.AlertManager:OnAlert" );
        return method != null ? AccessTools.MethodDelegate< BPCOnAlertDelegate >( method ) : null;
    }

    public static bool IsBPCOnAlert()
    {
        return bpcOnAlertDelegate != null ? bpcOnAlertDelegate() : false;
    }
}
