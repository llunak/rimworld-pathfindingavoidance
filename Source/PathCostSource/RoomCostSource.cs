using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using LudeonTK;

namespace PathfindingAvoidance;

// Make friendly visits avoid walking through rooms.
public class FriendlyRoomCostSource : PathCostSourceBase
{
    public FriendlyRoomCostSource(Map map)
        : base( map )
    {
    }

    public override void ComputeAll(IEnumerable<PathRequest> _)
    {
        costGrid.Clear();
        CellIndices cellIndices = map.cellIndices;
        foreach( Room room in map.regionGrid.AllRooms )
        {
            ushort roomCost = GetFriendlyRoomCost( room );
            if( roomCost > 0 )
            {
                foreach( IntVec3 pos in room.Cells )
                    costGrid[ cellIndices.CellToIndex( pos ) ] = roomCost;
            }
        }
    }

    public override bool UpdateIncrementally(IEnumerable<PathRequest> _, List<IntVec3> cellDeltas)
    {
        CellIndices cellIndices = map.cellIndices;
        foreach( IntVec3 cellDelta in cellDeltas )
        {
            Room room = cellDelta.GetRoom( map );
            ushort roomCost = GetFriendlyRoomCost( room );
            costGrid[ cellIndices.CellToIndex( cellDelta ) ] = roomCost;
        }
        return false;
    }

    private static ushort GetFriendlyRoomCost( Room room )
    {
        if( room == null )
            return 0;
        if( room.IsHuge || room.IsDoorway )
            return 0;
        if( room.PsychologicallyOutdoors )
            return (ushort) PathfindingAvoidanceMod.settings.visitingCaravanOutdoorsRoomCost;
        return (ushort) PathfindingAvoidanceMod.settings.visitingCaravanIndoorRoomCost;
    }
}
