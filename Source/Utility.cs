using HarmonyLib;
using Verse;
using System;
using System.Reflection;
using System.Diagnostics;

namespace PathfindingAvoidance;

public class Utility
{
    // A hook allowing mods to add additional pawn types
    // to be treated the same way as colonists.
    public static bool ShouldAlsoTreatAsColonist( Pawn pawn )
    {
        return false;
    }

    // Better Pawn Control emergency mode.
    private delegate bool BPCOnAlertDelegate();
    private static BPCOnAlertDelegate bpcOnAlertDelegate = InitBPCOnAlertDelegate();
    private static BPCOnAlertDelegate InitBPCOnAlertDelegate()
    {
        Type type = AccessTools.TypeByName( "BetterPawnControl.AlertManager" );
        if( type == null )
            return null;
        MethodInfo method = AccessTools.PropertyGetter( type, "OnAlert" );
        if( method == null )
            return null;
        return AccessTools.MethodDelegate< BPCOnAlertDelegate >( method );
    }

    public static bool IsBPCOnAlert()
    {
        return bpcOnAlertDelegate != null ? bpcOnAlertDelegate() : false;
    }
}

public static class Trace
{
    [Conditional("TRACE")]
    public static void Log(string message) => Verse.Log.Message("Pathfinding Avoidance Trace: " + message);
}
