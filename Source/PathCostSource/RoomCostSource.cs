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
        map.events.RegionsRoomsChanged += () => RegionsRoomsChanged();
    }

    public static bool IsEnabled()
    {
        return PathfindingAvoidanceMod.settings.visitingCaravanOutdoorsRoomCost != 0
            || PathfindingAvoidanceMod.settings.visitingCaravanIndoorRoomCost != 0;
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

    public override bool UpdateIncrementally(IEnumerable<PathRequest> requests, List<IntVec3> cellDeltas)
    {
        if( allChanged )
        {
            ComputeAll( requests );
            return true;
        }
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

    private void RegionsRoomsChanged()
    {
        // There is no info about cells affected, so dirty the entire grid.
        // A possible way to find out the changed cells would be to prefix and postfix
        // RegionAndRoomUpdater.CreateOrUpdateRooms() and do a diff between room cells,
        // but that might possibly turn out to be actually more expensive in the end
        // than updating everything.
        allChanged = true;
    }
}
