using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using System.Collections.Generic;
using Unity.Collections;
using LudeonTK;

namespace PathfindingAvoidance;

[HarmonyPatch(typeof(PathFinderMapData))]
public static class PathFinderMapData_Patch
{
    // Handle attaching PathCostSource to PathFinderMapData.
    [HarmonyPostfix]
    [HarmonyPatch(MethodType.Constructor)]
    [HarmonyPatch(new Type[] { typeof( Map ) })]
    public static void Constructor( PathFinderMapData __instance, Map map )
    {
        PathCostSourceHandler handler = null;
        foreach( PathType pathType in Enum.GetValues( typeof( PathType )))
        {
            if( pathType.IsEnabled())
            {
                if( handler == null )
                    handler = PathCostSourceHandler.Get( __instance );
            }
        }
        // TODO move all added triggers to their specific sources
        map.events.RegionsRoomsChanged += () => RegionsRoomsChanged( __instance );
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Dispose))]
    public static void Dispose(PathFinderMapData __instance)
    {
        PathCostSourceHandler.RemoveMap( __instance );
    }

    private static void RegionsRoomsChanged( PathFinderMapData mapData )
    {
        // If a room changes, need to update costs. There is no info about cells affected,
        // so dirty the entire map if needed.
        if( FriendlyRoomCostSource.IsEnabled())
            mapData.Notify_MapDirtied();
    }

    private static FieldInfo pathRequestCustomizer = AccessTools.Field( typeof( PathRequest ), "customizer" );

    // PathFinderMapData.ParameterizeGridJob() uses PathRequest.customizer instead of MapGridRequest.customizer.
    // It doesn't make a difference for vanilla, but it does not use our overriden customizer. Since conceptually
    // it seems incorrect to use the PathRequest one here (other things are read from MapGridRequest), simply fix that.
    [HarmonyTranspiler]
    [HarmonyPatch(nameof(ParameterizeGridJob))]
    public static IEnumerable<CodeInstruction> ParameterizeGridJob(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);
        bool found = false;
        for( int i = 0; i < codes.Count; ++i )
        {
            // Log.Message("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
            // The function has code:
            // request.customizer
            // Change to:
            // query.customizer
            if( codes[ i ].opcode == OpCodes.Ldarg_1 && i + 1 < codes.Count && codes[ i + 1 ].LoadsField( pathRequestCustomizer ))
            {
                codes[ i ] = new CodeInstruction( OpCodes.Ldarg_2 ).MoveLabelsFrom( codes[ i ] );
                codes[ i + 1 ] = CodeInstruction.LoadField( typeof( PathFinder.MapGridRequest ), "customizer" );
                found = true;
            }
        }
        if(!found)
            Log.Error( "PathfindingAvoidance: Failed to patch PathFinderMapData.ParameterizeGridJob()");
        return codes;
    }
}

// Update if a zone changes.
[HarmonyPatch(typeof(Zone))]
public static class Zone_Patch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(AddCell))]
    public static void AddCell(Zone __instance, IntVec3 c)
    {
        if( ZoneCostSource.IsEnabledAny( __instance ))
            __instance.Map.pathFinder.MapData.Notify_CellDelta( c );
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(RemoveCell))]
    public static void RemoveCell(Zone __instance, IntVec3 c)
    {
        if( ZoneCostSource.IsEnabledAny( __instance ))
            __instance.Map.pathFinder.MapData.Notify_CellDelta( c );
    }
}

// Need to override the customizer in created MapGridRequest objects.
[HarmonyPatch(typeof(PathFinder))]
public static class PathFinder_Patch
{
    // All these functions call MapGridRequest.Get(), and since that method does not have any reference to the outside
    // and we need PathFinderMapData, patch all callers.
    [HarmonyTargetMethods]
    private static IEnumerable<MethodBase> TargetMethod()
    {
        Type type = typeof(PathFinder);
        yield return AccessTools.Method(type, "ScheduleBatchedPathJobs");
        yield return AccessTools.Method(type, "ScheduleGridJobs");
        yield return AccessTools.Method(type, "ScheduleGridJob");
        yield return AccessTools.Method(type, "FindPathNow", new Type[] { typeof(IntVec3), typeof(LocalTargetInfo), typeof(TraverseParms),
            typeof(PathFinderCostTuning?), typeof(PathEndMode), typeof(PathRequest.IPathGridCustomizer) } );
    }

    private static MethodInfo forMethod = AccessTools.Method(typeof(PathFinder.MapGridRequest), "For");

    [HarmonyTranspiler]
    public static IEnumerable<CodeInstruction> Transpiller(IEnumerable<CodeInstruction> instructions, MethodBase __originalMethod)
    {
        var codes = new List<CodeInstruction>(instructions);
        bool found = false;
        for( int i = 0; i < codes.Count; ++i )
        {
            // Log.Message("T:" + i + ":" + codes[i].opcode + "::" + (codes[i].operand != null ? codes[i].operand.ToString() : codes[i].operand));
            // The function has code:
            // MapGridRequest gridRequest = MapGridRequest.For(pathRequest);
            // Change to:
            // MapGridRequest gridRequest = Transpiler_Hook(MapGridRequest.For(pathRequest), pathRequest, this);
            if(( codes[ i ].IsLdloc() ||  codes[ i ].IsLdarg())
                 && i + 1 < codes.Count && codes[ i + 1 ].Calls( forMethod ))
            {
                codes.Insert( i + 2, codes[ i ].Clone()); // load 'pathRequest'
                codes.Insert( i + 3, new CodeInstruction( OpCodes.Ldarg_0 )); // load 'this'
                codes.Insert( i + 4, new CodeInstruction( OpCodes.Call, typeof(PathFinder_Patch).GetMethod(nameof(Transpiler_Hook))));
                found = true;
                break;
            }
        }
        if(!found)
            Log.Error( "PathfindingAvoidance: Failed to patch " + __originalMethod);
        return codes;
    }

    public static PathFinder.MapGridRequest Transpiler_Hook( PathFinder.MapGridRequest gridRequest, PathRequest pathRequest, PathFinder pathFinder )
    {
        Pawn pawn = pathRequest.pawn;
        PathType pathType = PathTypeUtils.GetPathType( pathRequest );
        if( pathType != PathType.None )
            gridRequest.customizer = Customizer.Get( pathType, pathFinder.mapData, gridRequest.customizer );
        return gridRequest;
    }
}

[HarmonyPatch(typeof(PathFinder))]
public static class PathFinder2_Patch
{
    // This is called when grids change, destroy our cached data.
    [HarmonyPostfix]
    [HarmonyPatch(nameof(RecycleGridJobData))]
    public static void RecycleGridJobData( PathFinder __instance )
    {
        Customizer.ClearMap( __instance.mapData );
    }
}
