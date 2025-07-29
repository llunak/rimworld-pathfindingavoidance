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
using Unity.Collections.LowLevel.Unsafe;

namespace PathfindingAvoidance;

// RimWorld allows adding perceived path cost to cells, but it's somewhat difficult to use from a mod.
// We use IPathFinderDataSource-based PathCostSource instances to calculate extra costs for each cell.
// Adding the cost to pathfinding is done using PathRequest.IPathGridCustomizer-based Customizer class,
// which merges all costs for each cell.
// It serves two purposes:
//   - It provides the grid of costs to the pathfinding code. Since there can be only one customizer,
//     it also needs to wrap the previous one and add it to the grid it provides (created by merging
//     all the costs from IPathFinderDataSource-based providers).
//   - Make pathfinding code differentiate between different cost setups. Path calculations are cached
//     with PathFinder.MapGridRequest used as a key, and the customizer field seems to be the only
//     reasonable field there for a mod to make two keys different based on custom criteria.

using CustomizerMap = System.Collections.Generic.Dictionary< ( PathType, PathFinderMapData, PathRequest.IPathGridCustomizer ), Customizer >;

public class Customizer : PathRequest.IPathGridCustomizer, IDisposable
{
    // Change to other PathType to enable drawing of costs.
    // Note that the calculation is done only on demand, so the drawing is usually delayed (happens only when
    // next relevant pawn starts pathing somewhere).
    private const PathType DEBUG_TYPE = PathType.None;

    private readonly PathType pathType;
    private readonly PathFinderMapData mapData;
    private readonly PathRequest.IPathGridCustomizer original = null;
    private NativeArray<ushort> grid;
    private PathCostSourceBase[] sources = null;
    private unsafe ushort** sourcesPtrs = null; // NativeArray of pointers is apparently not possible, do it directly
    private HashSet< IntVec3 > needUpdateCells = [];
    private bool needUpdateAll = false;

    private static CustomizerMap customizerMap = new CustomizerMap();

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
        this.mapData = mapData;
        this.original = original;
        grid = new NativeArray<ushort>(mapData.map.cellIndices.NumGridCells, Allocator.Persistent);
        InitSources( mapData );
    }

    private void InitSources( PathFinderMapData mapData )
    {
        PathCostSourceHandler handler = PathCostSourceHandler.Get( mapData );
        sources = handler.GetSources( pathType ).ToArray();
        int sourcesLength = sources.Length + ( original != null ? 1 : 0 );
        unsafe
        {
            sourcesPtrs = (ushort**) UnsafeUtility.Malloc( sourcesLength * sizeof(ushort*), sizeof(ushort*), Allocator.Persistent );
        }
    }

    public NativeArray<ushort> GetOffsetGrid()
    {
        if( needUpdateAll )
            UpdateAllCells();
        else if( needUpdateCells.Count != 0 )
            UpdateCells();
        return grid;
    }

    // When any of the sources has changes in cells, this gets called (by our patch for PathFinderMapData).
    // There's no notification for changed in the original customizer, but at least existing vanilla customizers
    // (as of 1.6) do not change their data.
    public static void CellsNeedUpdate( PathFinderMapData mapData, HashSet< IntVec3 > updatedCells, bool updateAll )
    {
        foreach( var item in customizerMap )
        {
            if( item.Key.Item2 == mapData )
            {
                Customizer customizer = item.Value;
                customizer.needUpdateAll = customizer.needUpdateAll || updateAll;
                if( !customizer.needUpdateAll ) // No point in updating cells if all need update.
                    customizer.needUpdateCells.UnionWith( updatedCells );
            }
        }
    }

    public void Dispose()
    {
        grid.Dispose();
        unsafe
        {
            if( sourcesPtrs != null )
                UnsafeUtility.Free( sourcesPtrs, Allocator.Persistent );
            sourcesPtrs = null;
        }
    }

    public static void ClearMap( PathFinderMapData mapData )
    {
        foreach( var key in customizerMap.Keys.ToArray())
        {
            // When PathFinder resets its grids, dispose only customizers that wrap another customizer,
            // in order to release the reference to that customizer. The generic customizer that
            // does not wrap does not need disposing here, because it will update its grid as a result
            // of CellsNeedUpdate().
            if( key.Item2 == mapData && key.Item3 != null )
            {
                customizerMap[ key ].Dispose();
                customizerMap.Remove( key );
            }
        }
    }

    private void UpdateAllCells()
    {
#if false
        for( int i = 0; i < grid.Length; ++i )
        {
            int sum = 0;
            for( int j = 0; j < sources.Count(); ++j )
                sum += sources[ j ].CostGrid[ i ];
            if( original != null )
                sum += original.GetOffsetGrid()[ i ];
            grid[ i ] = sum > ushort.MaxValue ? ushort.MaxValue : (ushort) sum;
        }
#else
        // This is an optimized equivalent of the above, seems to be about 4x faster.
        unsafe
        {
            ushort* gridPtr = (ushort*) NativeArrayUnsafeUtility.GetUnsafePtr( grid );
            int sourcesLength = sources.Length;
            for( int i = 0; i < sourcesLength; ++i )
                sourcesPtrs[ i ] = (ushort*) NativeArrayUnsafeUtility.GetUnsafePtr( sources[ i ].CostGrid );
            if( original != null )
            {
                sourcesPtrs[ sourcesLength ] = (ushort*) NativeArrayUnsafeUtility.GetUnsafePtr( original.GetOffsetGrid());
                ++sourcesLength;
            }
            int gridLength = grid.Length;
            for( int i = 0; i < gridLength; ++i )
            {
                int sum = 0;
                for( int j = 0; j < sourcesLength; ++j )
                    sum += *(sourcesPtrs[ j ]++);
                *(gridPtr++) = sum > ushort.MaxValue ? ushort.MaxValue : (ushort) sum;
            }
        }
#endif
    if( pathType == DEBUG_TYPE )
    {
        CellIndices cellIndices = mapData.map.cellIndices;
        mapData.map.debugDrawer.debugCells.Clear(); // FlashCell() adds unconditionally, so remove old, they'll be overwritten
        for( int i = 0; i < grid.Length; ++i )
        {
            IntVec3 cell = cellIndices.IndexToCell( i );
            // TODO use a better mapping for the cost range
            mapData.map.debugDrawer.FlashCell( cell, grid[ i ] / 2000f, grid[ i ].ToString());
        }
    }
    needUpdateAll = false;
    needUpdateCells.Clear();
    }

    private void UpdateCells()
    {
        CellIndices cellIndices = mapData.map.cellIndices;
#if false
        foreach( IntVec3 cell in needUpdateCells )
        {
            int i = cellIndices.CellToIndex( cell );
            int sum = 0;
            for( int j = 0; j < sources.Count(); ++j )
                sum += sources[ j ].CostGrid[ i ];
            if( original != null )
                sum += original.GetOffsetGrid()[ i ];
            grid[ i ] = sum > ushort.MaxValue ? ushort.MaxValue : (ushort) sum;
        }
#else
        // This is an optimized equivalent of the above.
        // TODO Is this worth it?
        unsafe
        {
            ushort* gridPtr = (ushort*) NativeArrayUnsafeUtility.GetUnsafePtr( grid );
            int sourcesLength = sources.Length;
            for( int i = 0; i < sourcesLength; ++i )
                sourcesPtrs[ i ] = (ushort*) NativeArrayUnsafeUtility.GetUnsafePtr( sources[ i ].CostGrid );
            if( original != null )
            {
                sourcesPtrs[ sourcesLength ] = (ushort*) NativeArrayUnsafeUtility.GetUnsafePtr( original.GetOffsetGrid());
                ++sourcesLength;
            }
            foreach( IntVec3 cell in needUpdateCells )
            {
                int i = cellIndices.CellToIndex( cell );
                int sum = 0;
                for( int j = 0; j < sourcesLength; ++j )
                    sum += sourcesPtrs[ j ][ i ];
                gridPtr[ i ] = sum > ushort.MaxValue ? ushort.MaxValue : (ushort) sum;
            }
        }
#endif
    if( pathType == DEBUG_TYPE )
    {
        foreach( IntVec3 cell in needUpdateCells )
        {
            int num = cellIndices.CellToIndex( cell );
            mapData.map.debugDrawer.debugCells.RemoveAll( ( DebugCell c ) => c.c == cell );
            mapData.map.debugDrawer.FlashCell( cell, grid[ num ] / 2000f, grid[ num ].ToString());
        }
    }
    needUpdateAll = false;
    needUpdateCells.Clear();
    }
}
