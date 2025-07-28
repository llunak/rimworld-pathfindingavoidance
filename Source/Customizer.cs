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

using CustomizerMap = System.Collections.Generic.Dictionary< ( PathType, PathFinderMapData, PathRequest.IPathGridCustomizer ), Customizer >;

public class Customizer : PathRequest.IPathGridCustomizer, IDisposable
{
    private readonly PathType pathType;
    private readonly PathRequest.IPathGridCustomizer original = null;
    private NativeArray<ushort> grid;
    private PathCostSourceBase[] sources = null;

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
        this.original = original;
        grid = new NativeArray<ushort>(mapData.map.cellIndices.NumGridCells, Allocator.Persistent);
        InitSources( mapData );
    }

    private void InitSources( PathFinderMapData mapData )
    {
        PathCostSourceHandler handler = PathCostSourceHandler.Get( mapData );
        sources = handler.GetSources( pathType ).ToArray();
    }

    public NativeArray<ushort> GetOffsetGrid()
    {
        UpdateGrid();
        return grid;
    }

    public void Dispose()
    {
        grid.Dispose();
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

    private void UpdateGrid()
    {
        for( int i = 0; i < grid.Length; ++i )
        {
            grid[ i ] = 0;
            for( int j = 0; j < sources.Count(); ++j )
                grid[ i ] += sources[ j ].CostGrid[ i ];
        }
    }
}
