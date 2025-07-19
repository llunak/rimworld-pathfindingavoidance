using HarmonyLib;
using RimWorld;
using Verse;
using Verse.AI;
using System;
using System.Collections.Generic;
using Unity.Collections;
using LudeonTK;

namespace PathfindingAvoidance;

// Based on PerceptualSource, calculate extra perceived path cost for cells
// which have filth-generating terrain.
public class PathCostSource : IPathFinderDataSource, IDisposable
{
    private readonly Map map;

    private NativeArray<ushort> cost;

    public NativeArray<ushort> Cost => cost;

    public PathCostSource(Map map)
    {
        this.map = map;
        int numGridCells = map.cellIndices.NumGridCells;
        cost = new NativeArray<ushort>(numGridCells, Allocator.Persistent);
    }

    public void Dispose()
    {
        cost.Dispose();
    }

    public void ComputeAll(IEnumerable<PathRequest> _)
    {
        cost.Clear();
        TerrainGrid terrainGrid = map.terrainGrid;
        for( int i = 0; i < map.cellIndices.NumGridCells; ++i )
        {
            TerrainDef terrainDef = terrainGrid.TerrainAt( i );
            if( terrainDef != null )
                cost[i] = CalculateCellCost( terrainDef );
        }
    }

    public bool UpdateIncrementally(IEnumerable<PathRequest> _, List<IntVec3> cellDeltas)
    {
        CellIndices cellIndices = map.cellIndices;
        TerrainGrid terrainGrid = map.terrainGrid;
        foreach( IntVec3 cellDelta in cellDeltas )
        {
            int num = cellIndices.CellToIndex( cellDelta );
            TerrainDef terrainDef = terrainGrid.TerrainAt( num );
            if (terrainDef != null)
                cost[ num ] = CalculateCellCost( terrainDef );
            else
                cost[ num ] = 0;
        }
        return false;
    }

    private ushort CalculateCellCost( TerrainDef terrainDef )
    {
        ushort cost = 0;
        if( terrainDef.generatedFilth != null )
            cost += (ushort) PathfindingAvoidanceMod.settings.dirtyCost;
        return cost;
    }
}
