using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using LudeonTK;
using System.Runtime.CompilerServices;

namespace PathfindingAvoidance;

// Make friendly visits avoid walking through rooms.
public class FriendlyRoomCostSource : PathCostSourceBase
{
    public FriendlyRoomCostSource(Map map)
        : base( map )
    {
        map.events.RegionsRoomsChanged += RegionsRoomsChanged;
        map.events.RoofChanged += RoofChanged;
    }

    public static bool IsEnabled()
    {
        return PathfindingAvoidanceMod.settings.visitingCaravanOutdoorsRoomCost != 0
            || PathfindingAvoidanceMod.settings.visitingCaravanIndoorRoomCost != 0;
    }

    public override void ComputeAll(IEnumerable<PathRequest> _)
    {
        Trace.Log("Updating all cells for RoomCostSource, map: " + map);
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
        Trace.Log("Updating " + cellDeltas.Count + "+" + extraChangedCells.Count + " cells for RoomCostSource, map: " + map);
        CellIndices cellIndices = map.cellIndices;
        var updateCell = ( IntVec3 cell ) =>
        {
            Room room = cell.GetRoom( map );
            ushort roomCost = GetFriendlyRoomCost( room );
            costGrid[ cellIndices.CellToIndex( cell ) ] = roomCost;
        };
        foreach( IntVec3 cellDelta in cellDeltas )
            updateCell( cellDelta );
        foreach( IntVec3 cell in extraChangedCells )
            updateCell( cell );
        bool result = extraChangedCells.Count != 0;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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

    private void RoofChanged( IntVec3 cell )
    {
        if( allChanged )
            return;
        if( !map.regionAndRoomUpdater.Enabled )
        {
            // There's a logged warning if room updater is disabled, do a full update if room information
            // is not up to date.
            allChanged = true;
            return;
        }
        Room room = cell.GetRoom( map );
        if( room == null )
            return;
        // If room inside a room changes, need to update all the room's cells, because
        // that may change the cost inside of the room (PsychologicallyOutdoors).
        foreach( IntVec3 pos in room.Cells )
            extraChangedCells.Add( pos );
    }
}
