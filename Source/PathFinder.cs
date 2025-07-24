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

// RimWorld allows adding perceived path cost to cells, but it's somewhat difficult to use from a mod.
// We use IPathFinderDataSource-based PathCostSource to calculate extra cost for each cell.
// Adding the cost to pathfinding is done using PathRequest.IPathGridCustomizer-based Customizer class.
// It serves two purposes:
//   - It provides the grid of costs to the pathfinding code. Since there can be only one customizer,
//     it also needs to wrap the previous one and add it to the grid it provides.
//   - Make pathfinding code differentiate between different cost setups. Path calculations are cached
//     with PathFinder.MapGridRequest used as a key, and the customizer field seems to be the only
//     reasonable field there for a mod to make two keys different based on custom criteria.

using SourceMap = System.Collections.Generic.Dictionary< ( PathType, PathFinderMapData ), PathCostSource >;
using CustomizerMap = System.Collections.Generic.Dictionary< ( PathType, PathFinderMapData, PathRequest.IPathGridCustomizer ), Customizer >;

[HarmonyPatch(typeof(PathFinderMapData))]
public static class PathFinderMapData_Patch
{
    // Handle attaching PathCostSource to PathFinderMapData.
    [HarmonyPostfix]
    [HarmonyPatch(MethodType.Constructor)]
    [HarmonyPatch(new Type[] { typeof( Map ) })]
    public static void Constructor( PathFinderMapData __instance, Map map )
    {
        foreach( PathType pathType in Enum.GetValues( typeof( PathType )))
        {
            if( pathType.IsEnabled())
            {
                PathCostSource source = new PathCostSource( map, pathType );
                Customizer.AddSource( pathType, __instance, source );
                __instance.RegisterSource( source );
            }
        }
        map.events.RegionsRoomsChanged += () => RegionsRoomsChanged( __instance );
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Dispose))]
    public static void Dispose(PathFinderMapData __instance)
    {
        Customizer.RemoveMap( __instance );
    }

    private static void RegionsRoomsChanged( PathFinderMapData mapData )
    {
        // If a room changes, need to update costs. There is no info about cells affected,
        // so dirty the entire map if needed.
        if( PathCostSource.IsEnabledRooms())
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
        if( PathCostSource.IsEnabledZonesAny( __instance ))
            __instance.Map.pathFinder.MapData.Notify_CellDelta( c );
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(RemoveCell))]
    public static void RemoveCell(Zone __instance, IntVec3 c)
    {
        if( PathCostSource.IsEnabledZonesAny( __instance ))
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

// The class that provides grid data to pathfinding. Needs to wrap the previous customizer if present.
public class Customizer : PathRequest.IPathGridCustomizer, IDisposable
{
    private readonly PathType pathType;
    private readonly PathRequest.IPathGridCustomizer original = null;
    private readonly PathCostSource source = null;
    private NativeArray<ushort> grid;
    private int sourceLastUpdateId = 0;

    private static CustomizerMap customizerMap = new CustomizerMap();
    private static SourceMap sourceMap = new SourceMap();

    private bool IsWrapper => original != null;

    public static Customizer Get( PathType pathType, PathFinderMapData mapData, PathRequest.IPathGridCustomizer original )
    {
        Customizer customizer;
        if( customizerMap.TryGetValue( ( pathType, mapData, original ), out customizer ))
            return customizer;
        customizer = new Customizer( pathType, mapData, original );
        customizerMap[ ( pathType, mapData, original ) ] = customizer;
        return customizer;
    }

    public Customizer(PathType pathType, PathFinderMapData mapData, PathRequest.IPathGridCustomizer original)
    {
        this.pathType = pathType;
        this.original = original;
        source = sourceMap[ ( pathType, mapData ) ];
        if( !IsWrapper )
        {
            // Simple case, we do not need to chain an original customizer, so just use PathCostSource data.
            // As I understand it, NativeArray is essentially a struct containing a pointer, so assignment
            // is cheap and shares the data pointed to. This also means we do not need the update
            // anything if the grid in PathCostSource changes, because it's still the same buffer.
            // PathCostSource is only disposed when map is removed, so lifetime is also fine.
            grid = source.Cost;
        }
        else
        {
            // If there is a customizer to wrap, compute a new grid from both.
            grid = new NativeArray<ushort>(mapData.map.cellIndices.NumGridCells, Allocator.Persistent);
            MergeWrapperGrid();
        }
    }

    public NativeArray<ushort> GetOffsetGrid()
    {
        // No need to update in !IsWrapper case (see above).
        // In IsWrapper case, use LastUpdateId to detect when PathCostSource.UpdateIncrementally()
        // updates but returns false (so only grid data changes in the array but no caching is disposed).
        // I don't know how to detect that the original customizer has updated in such a way,
        // but vanilla ones do not update, so that should be safe. Since this is not called _that_ often,
        // maybe using UnsafeUtility.MemCmp() to detect a change could do, although it might be better
        // to have some interface that provides update id (since any customizer needing this should
        // be only another mod).
        if( IsWrapper && sourceLastUpdateId != source.LastUpdateId )
            MergeWrapperGrid();
        return grid;
    }

    private void MergeWrapperGrid()
    {
        NativeArray<ushort> originalGrid = original.GetOffsetGrid();
        NativeArray<ushort> sourceGrid = source.Cost;
        for( int i = 0; i < grid.Count(); ++i )
            grid[ i ] = (ushort)Math.Clamp( sourceGrid[ i ] + originalGrid[ i ], 0, 65535);
        sourceLastUpdateId = source.LastUpdateId;
    }

    public void Dispose()
    {
        if( IsWrapper )
            grid.Dispose();
    }

    public static void AddSource( PathType pathType, PathFinderMapData mapData, PathCostSource source )
    {
        sourceMap[ ( pathType, mapData ) ] = source;
    }

    public static void ClearMap( PathFinderMapData mapData )
    {
        foreach( var key in customizerMap.Keys.ToArray())
        {
            if( key.Item2 == mapData )
            {
                customizerMap[ key ].Dispose();
                customizerMap.Remove( key );
            }
        }
    }

    public static void RemoveMap( PathFinderMapData mapData )
    {
        ClearMap( mapData );
        foreach( var key in sourceMap.Keys.ToArray())
        {
            if( key.Item2 == mapData )
                sourceMap.Remove( key ); // No Dispose here(), PathFinderMapData does that.
        }
    }
}
