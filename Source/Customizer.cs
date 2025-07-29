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
        unsafe
        {
            sourcesPtrs = (ushort**) UnsafeUtility.Malloc( sources.Length * sizeof(ushort*), sizeof(ushort*), Allocator.Persistent );
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
            grid[ i ] = sum > ushort.MaxValue ? ushort.MaxValue : (ushort) sum;
        }
#else
        // This is an optimized equivalent of the above, seems to be about 4x faster.
        unsafe
        {
            ushort* gridPtr = (ushort*) NativeArrayUnsafeUtility.GetUnsafePtr( grid );
            for( int i = 0; i < sources.Length; ++i )
                sourcesPtrs[ i ] = (ushort*) NativeArrayUnsafeUtility.GetUnsafePtr( sources[ i ].CostGrid );
            int gridLength = grid.Length;
            for( int i = 0; i < gridLength; ++i )
            {
                int sum = 0;
                int sourcesCount = sources.Count();
                for( int j = 0; j < sourcesCount; ++j )
                    sum += *(sourcesPtrs[ j ]++);
                *(gridPtr++) = sum > ushort.MaxValue ? ushort.MaxValue : (ushort) sum;
            }
        }
#endif
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
            grid[ i ] = sum > ushort.MaxValue ? ushort.MaxValue : (ushort) sum;
        }
#else
        // This is an optimized equivalent of the above.
        // TODO Is this worth it?
        unsafe
        {
            ushort* gridPtr = (ushort*) NativeArrayUnsafeUtility.GetUnsafePtr( grid );
            for( int i = 0; i < sources.Length; ++i )
                sourcesPtrs[ i ] = (ushort*) NativeArrayUnsafeUtility.GetUnsafePtr( sources[ i ].CostGrid );
            foreach( IntVec3 cell in needUpdateCells )
            {
                int i = cellIndices.CellToIndex( cell );
                int sum = 0;
                int sourcesCount = sources.Count();
                for( int j = 0; j < sourcesCount; ++j )
                    sum += sourcesPtrs[ j ][ i ];
                gridPtr[ i ] = sum > ushort.MaxValue ? ushort.MaxValue : (ushort) sum;
            }
        }
#endif
    needUpdateAll = false;
    needUpdateCells.Clear();
    }
}
