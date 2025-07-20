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

using CustomizerMap = System.Collections.Generic.Dictionary< ( PathFinderMapData, PathRequest.IPathGridCustomizer ), Customizer >;

[HarmonyPatch(typeof(PathFinderMapData))]
public static class PathFinderMapData_Patch
{
    // Handle attaching PathCostSource to PathFinderMapData.
    [HarmonyPostfix]
    [HarmonyPatch(MethodType.Constructor)]
    [HarmonyPatch(new Type[] { typeof( Map ) })]
    public static void Constructor( PathFinderMapData __instance, Map map )
    {
        PathCostSource source = new PathCostSource( map );
        Customizer.AddSource( __instance, source );
        __instance.RegisterSource( source );
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(Dispose))]
    public static void Dispose(PathFinderMapData __instance)
    {
        Customizer.RemoveMap( __instance );
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
            }
        }
        if(!found)
            Log.Error( "PathfindingAvoidance: Failed to patch " + __originalMethod);
        return codes;
    }

    public static PathFinder.MapGridRequest Transpiler_Hook( PathFinder.MapGridRequest gridRequest, PathRequest pathRequest, PathFinder pathFinder )
    {
        Pawn pawn = pathRequest.pawn;
        if( ShouldApplyCost( pathRequest ))
            gridRequest.customizer = Customizer.Get( pathFinder.mapData, gridRequest.customizer );
        return gridRequest;
    }

    private static bool ShouldApplyCost( PathRequest request )
    {
        // TODO Does this need optimizing?
        // TODO Save last pawn/tick and cache result (this gets called several times in a row).
        // This doesn't actually seem to be called that often.
        Pawn pawn = request.pawn;
        if( pawn.IsAnimal )
            return false;
        if( pawn.IsPlayerControlled )
        {
            // Player-controlled pawns (colonists, mechs) generally follow the rules,
            // with some exceptions.
            if( pawn.Drafted || pawn.Crawling )
                return false;
            // Some things inspired by GatheringsUtility.ShouldGuestKeepAttendingGathering().
            if( pawn.health.hediffSet.BleedRateTotal > 0.3f || pawn.health.hediffSet.InLabor())
                return false;
            // Carrying another downed pawn (but not a baby).
            if( pawn.carryTracker != null && pawn.carryTracker.CarriedThing is Pawn otherPawn
                && otherPawn.Downed && !otherPawn.DevelopmentalStage.Baby())
            {
                    return false;
            }
            if( pawn.InMentalState )
                return false;
            return true;
        }
        // Raiders never follow rules.
        Faction mapFaction = request.map.ParentFaction ?? null;
        if( pawn.Faction != mapFaction && pawn.Faction != null && pawn.Faction.HostileTo( mapFaction ))
            return false;
        // Neutrals follow rules if not in mental state or in fight.
        if( pawn.InMentalState
            || pawn.mindState?.meleeThreat != null
            || pawn.mindState?.enemyTarget != null
            /*|| ( pawn.mindState?.WasRecentlyCombatantTicks( 10 ) ?? false ) does not work unfortunately*/)
        {
            return false;
        }
        return true;
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
    private NativeArray<ushort> grid;
    private PathRequest.IPathGridCustomizer original = null;
    private PathCostSource source = null;
    private int sourceLastUpdateId = 0;

    private static CustomizerMap customizerMap = new CustomizerMap();
    private static Dictionary< PathFinderMapData, PathCostSource > sourceMap = new Dictionary< PathFinderMapData, PathCostSource >();

    private bool IsWrapper => original != null;

    public static Customizer Get( PathFinderMapData mapData, PathRequest.IPathGridCustomizer original )
    {
        Customizer customizer;
        if( customizerMap.TryGetValue( ( mapData, original ), out customizer ))
            return customizer;
        customizer = new Customizer( mapData, original );
        customizerMap[ ( mapData, original ) ] = customizer;
        return customizer;
    }

    public Customizer(PathFinderMapData mapData, PathRequest.IPathGridCustomizer original)
    {
        this.original = original;
        source = sourceMap[ mapData ];
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

    public static void AddSource( PathFinderMapData mapData, PathCostSource source )
    {
        sourceMap[ mapData ] = source;
    }

    public static void ClearMap( PathFinderMapData mapData )
    {
        foreach( var key in customizerMap.Keys.ToArray())
        {
            if( key.Item1 == mapData )
            {
                customizerMap[ key ].Dispose();
                customizerMap.Remove( key );
            }
        }
    }

    public static void RemoveMap( PathFinderMapData mapData )
    {
        ClearMap( mapData );
        sourceMap.Remove( mapData );
    }
}
