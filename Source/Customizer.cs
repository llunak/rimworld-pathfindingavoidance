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
