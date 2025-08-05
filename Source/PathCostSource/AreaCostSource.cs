using HarmonyLib;
using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using LudeonTK;
using System.Reflection;
using System.Runtime.CompilerServices;

namespace PathfindingAvoidance;

// Use areas "Path Avoid Low", "Path Avoid Medium" and "Path Avoid High", if they exist,
// to let the player manually add costs to cells.
public class AreaCostSource : PathCostSourceBase
{
    private static List< AreaCostSource > sources = []; // There won't be that many maps, List is fine.

    public AreaCostSource(Map map)
        : base( map )
    {
        sources.Add( this );
    }

    public static bool IsEnabled()
    {
        return PathfindingAvoidanceMod.settings.areaAvoidLowCost != 0
            || PathfindingAvoidanceMod.settings.areaAvoidMediumCost != 0
            || PathfindingAvoidanceMod.settings.areaAvoidHighCost != 0;
    }

    public override void ComputeAll(IEnumerable<PathRequest> _)
    {
        costGrid.Clear();
        CellIndices cellIndices = map.cellIndices;
        ( Area_Allowed areaLow, Area_Allowed areaMedium, Area_Allowed areaHigh ) = GetAreas();
        if( areaLow == null && areaMedium == null && areaHigh == null )
            return;
        var processArea = ( Area_Allowed area, ushort areaCost ) =>
        {
            foreach( IntVec3 cell in area.ActiveCells )
                costGrid[ cellIndices.CellToIndex( cell ) ] += areaCost;
        };
        if( areaLow != null )
            processArea( areaLow, (ushort) PathfindingAvoidanceMod.settings.areaAvoidLowCost );
        if( areaMedium != null )
            processArea( areaMedium, (ushort) PathfindingAvoidanceMod.settings.areaAvoidMediumCost );
        if( areaHigh != null )
            processArea( areaHigh, (ushort) PathfindingAvoidanceMod.settings.areaAvoidHighCost );
    }

    public override bool UpdateIncrementally(IEnumerable<PathRequest> requests, List<IntVec3> cellDeltas)
    {
        if( allChanged )
        {
            ComputeAll( requests );
            return true;
        }
        CellIndices cellIndices = map.cellIndices;
        ( Area_Allowed areaLow, Area_Allowed areaMedium, Area_Allowed areaHigh ) = GetAreas();
        if( areaLow == null && areaMedium == null && areaHigh == null )
        {
            var clearCell = ( IntVec3 cell ) =>
            {
                costGrid[ cellIndices.CellToIndex( cell ) ] = 0;
            };
            foreach( IntVec3 cellDelta in cellDeltas )
                clearCell( cellDelta );
            foreach( IntVec3 cell in extraChangedCells )
                clearCell( cell );
        }
        else
        {
            var updateCell = ( IntVec3 cell ) =>
            {
                int num = cellIndices.CellToIndex( cell );
                ushort cost = 0;
                if( areaLow != null && areaLow[ num ])
                    cost += (ushort) PathfindingAvoidanceMod.settings.areaAvoidLowCost;
                if( areaMedium != null && areaMedium[ num ])
                    cost += (ushort) PathfindingAvoidanceMod.settings.areaAvoidMediumCost;
                if( areaHigh != null && areaHigh[ num ])
                    cost += (ushort) PathfindingAvoidanceMod.settings.areaAvoidHighCost;
                costGrid[ num ] = cost;
            };
            foreach( IntVec3 cellDelta in cellDeltas )
                updateCell( cellDelta );
            foreach( IntVec3 cell in extraChangedCells )
                updateCell( cell );
        }
        bool result = extraChangedCells.Count != 0;
        return result;
    }

    public static string areaLowName = "Path Avoid Low";
    public static string areaMediumName = "Path Avoid Medium";
    public static string areaHighName = "Path Avoid High";

    private ( Area_Allowed, Area_Allowed, Area_Allowed ) GetAreas()
    {
        Area_Allowed areaLow = null;
        Area_Allowed areaMedium = null;
        Area_Allowed areaHigh = null;
        foreach( Area area in map.areaManager.AllAreas )
        {
            if( area.Label == areaLowName )
                areaLow = ( Area_Allowed ) area;
            else if( area.Label == areaMediumName )
                areaMedium = ( Area_Allowed ) area;
            else if( area.Label == areaHighName )
                areaHigh = ( Area_Allowed ) area;
        }
        return ( areaLow, areaMedium, areaHigh );
    }

    public override void Dispose()
    {
        base.Dispose();
        sources.Remove( this );
    }

    public static bool IsArea( Area area )
    {
        return area.Label == areaLowName
            || area.Label == areaMediumName
            || area.Label == areaHighName;
    }

    public static void CheckUpdate( Area area )
    {
        if( !IsArea( area ))
            return;
        foreach( AreaCostSource source in sources )
            if( source.map == area.Map )
                source.allChanged = true;
    }

    public static void CheckUpdate( Area area, IntVec3 cell )
    {
        if( !IsArea( area ))
            return;
        foreach( AreaCostSource source in sources )
            if( source.map == area.Map )
                source.extraChangedCells.Add( cell );
    }
}

[HarmonyPatch(typeof(Area_Allowed))]
public class Area_Allowed_Patch
{
    [HarmonyPostfix]
    [HarmonyPatch(MethodType.Constructor)]
    [HarmonyPatch(new Type[] { typeof( AreaManager ), typeof( string ) })]
    public static void Constructor( Area_Allowed __instance )
    {
        AreaCostSource.CheckUpdate( __instance );
    }

    // Disable assigning of the areas to any pawn.
    [HarmonyPrefix]
    [HarmonyPatch(nameof(AssignableAsAllowed))]
    public static bool AssignableAsAllowed( ref bool __result, Area_Allowed __instance )
    {
        if( AreaCostSource.IsArea( __instance ))
        {
            __result = false;
            return false;
        }
        return true;
    }

    // Notify if renamed to/from an area.
    [HarmonyPrefix]
    [HarmonyPatch(nameof(SetLabel))]
    public static void SetLabel( Area_Allowed __instance )
    {
        AreaCostSource.CheckUpdate( __instance );
    }
    [HarmonyPostfix]
    [HarmonyPatch(nameof(SetLabel))]
    public static void SetLabelPostfix( Area_Allowed __instance )
    {
        AreaCostSource.CheckUpdate( __instance );
    }
    [HarmonyPrefix]
    [HarmonyPatch(nameof(RenamableLabel), MethodType.Setter)]
    public static void RenamableLabel( Area_Allowed __instance )
    {
        AreaCostSource.CheckUpdate( __instance );
    }
    [HarmonyPostfix]
    [HarmonyPatch(nameof(RenamableLabel), MethodType.Setter)]
    public static void RenamableLabelPostfix( Area_Allowed __instance )
    {
        AreaCostSource.CheckUpdate( __instance );
    }
}

[HarmonyPatch(typeof(Area))]
public class Area_Patch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(MarkDirty))]
    public static void MarkDirty( Area __instance, IntVec3 c )
    {
        AreaCostSource.CheckUpdate( __instance, c );
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Delete))]
    public static void Delete( Area __instance )
    {
        AreaCostSource.CheckUpdate( __instance );
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Invert))]
    public static void Invert( Area __instance )
    {
        AreaCostSource.CheckUpdate( __instance );
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(Clear))]
    public static void Clear( Area __instance )
    {
        AreaCostSource.CheckUpdate( __instance );
    }
}

[HarmonyPatch]
public class AreaUtility_Patch
{
    [HarmonyTargetMethod]
    private static MethodBase TargetMethod()
    {
        Type nestedClass = typeof(AreaUtility).GetNestedType("<>c", BindingFlags.NonPublic);
        return AccessTools.Method(nestedClass, "<MakeAllowedAreaListFloatMenu>b__0_2");
    }

    // Make possible to change the area using "Expand/Clear Allowed Area" actions in the architect menu
    // (the only exception to AssignableAsAllowed()).
    [HarmonyPostfix]
    public static bool Postfix( bool result, Area a )
    {
        if( AreaCostSource.IsArea( a ))
            return true;
        return result;
    }
}
