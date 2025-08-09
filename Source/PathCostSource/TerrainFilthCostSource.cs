using Verse;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace PathfindingAvoidance;

// Avoid cells with terrain that creates filth.
public class TerrainFilthCostSource : PathCostSourceBase
{
    public TerrainFilthCostSource(Map map)
        : base( map )
    {
    }

    public static bool IsEnabled()
    {
        return PathfindingAvoidanceMod.settings.dirtyCost != 0;
    }

    public override void ComputeAll(IEnumerable<PathRequest> _)
    {
        Trace.Log("Updating all cells for TerrainFilthCostSource, map: " + map);
        TerrainGrid terrainGrid = map.terrainGrid;
        for( int i = 0; i < map.cellIndices.NumGridCells; ++i )
        {
            TerrainDef terrainDef = terrainGrid.TerrainAt( i );
            if( terrainDef != null )
                costGrid[i] = GetTerrainCost( terrainDef );
            else
                costGrid[i] = 0;
        }
    }

    public override bool UpdateIncrementally(IEnumerable<PathRequest> _, List<IntVec3> cellDeltas)
    {
        Trace.Log("Updating " + cellDeltas.Count + "+" + extraChangedCells.Count + " cells for TerrainFilthCostSource, map: " + map);
        CellIndices cellIndices = map.cellIndices;
        TerrainGrid terrainGrid = map.terrainGrid;
        foreach( IntVec3 cellDelta in cellDeltas )
        {
            int num = cellIndices.CellToIndex( cellDelta );
            TerrainDef terrainDef = terrainGrid.TerrainAt( num );
            costGrid[ num ] = GetTerrainCost( terrainDef );
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort GetTerrainCost( TerrainDef terrainDef )
    {
        if( terrainDef == null )
            return 0;
        if( terrainDef.generatedFilth != null )
            return (ushort) PathfindingAvoidanceMod.settings.dirtyCost;
        return 0;
    }
}
