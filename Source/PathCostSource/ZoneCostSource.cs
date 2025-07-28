using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using LudeonTK;

namespace PathfindingAvoidance;

// Avoid growing zones.
public class ZoneCostSource : PathCostSourceBase
{
    private readonly PathType pathType;

    public ZoneCostSource(Map map, PathType pathType)
        : base( map )
    {
        this.pathType = pathType;
    }

    public override void ComputeAll(IEnumerable<PathRequest> _)
    {
        costGrid.Clear();
        CellIndices cellIndices = map.cellIndices;
        foreach( Zone zone in map.zoneManager.AllZones )
        {
            ushort zoneCost = GetZoneCost( zone );
            if( zoneCost > 0 )
                // This needs to use 'cells' and not 'Cells', because the latter is not thread-safe.
                foreach( IntVec3 pos in zone.cells )
                    costGrid[ cellIndices.CellToIndex( pos ) ] = zoneCost;
        }
    }

    public override bool UpdateIncrementally(IEnumerable<PathRequest> _, List<IntVec3> cellDeltas)
    {
        CellIndices cellIndices = map.cellIndices;
        foreach( IntVec3 cellDelta in cellDeltas )
        {
            int num = cellIndices.CellToIndex( cellDelta );
            Zone zone = map.zoneManager.ZoneAt( cellDelta );
            ushort zoneCost = GetZoneCost( zone );
            costGrid[ num ] = zoneCost;
        }
        return false;
    }

    private ushort GetZoneCost( Zone zone )
    {
        if( !( zone is Zone_Growing ))
            return 0;
        return (ushort) PathfindingAvoidanceMod.settings.growingZoneCost[ (int)pathType ];
    }
}
