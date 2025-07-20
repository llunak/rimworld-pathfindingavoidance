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
    private int lastUpdateId = 0;

    public NativeArray<ushort> Cost => cost;
    public int LastUpdateId => lastUpdateId;

    public bool triggerRegenerate = false;

    private static List< PathCostSource > allSources = new List< PathCostSource >();

    public PathCostSource(Map map)
    {
        allSources.Add( this );
        this.map = map;
        int numGridCells = map.cellIndices.NumGridCells;
        cost = new NativeArray<ushort>(numGridCells, Allocator.Persistent);
    }

    public void Dispose()
    {
        cost.Dispose();
        allSources.Remove( this );
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
        ++lastUpdateId;
    }

    public bool UpdateIncrementally(IEnumerable<PathRequest> requests, List<IntVec3> cellDeltas)
    {
        if( triggerRegenerate )
        {
            triggerRegenerate = false;
            ComputeAll( requests );
            return true;
        }
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
        if( cellDeltas.Count != 0 )
            ++lastUpdateId;
        return false;
    }

    private ushort CalculateCellCost( TerrainDef terrainDef )
    {
        ushort cost = 0;
        if( terrainDef.generatedFilth != null )
            cost += (ushort) PathfindingAvoidanceMod.settings.dirtyCost;
        return cost;
    }

    public static void RegenerateAll()
    {
        foreach( PathCostSource source in allSources )
            source.triggerRegenerate = true;
    }
}
