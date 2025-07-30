using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using LudeonTK;
using System.Runtime.CompilerServices;

namespace PathfindingAvoidance;

// Avoid doors with configured priority.
public class DoorCostSource : PathCostSourceBase
{
    public DoorCostSource(Map map)
        : base( map )
    {
    }

    public static bool IsEnabled()
    {
        return PathfindingAvoidanceMod.settings.sideDoorCost != 0
            || PathfindingAvoidanceMod.settings.emergencyDoorCost != 0;
    }

    public override void ComputeAll(IEnumerable<PathRequest> _)
    {
        costGrid.Clear();
        CellIndices cellIndices = map.cellIndices;
        foreach( Building_Door door in map.listerBuildings.AllBuildingsColonistOfClass< Building_Door >())
        {
            ushort doorCost = GetDoorCost( door );
            if( doorCost > 0 )
                foreach( IntVec3 pos in door.OccupiedRect().Cells )
                    costGrid[ cellIndices.CellToIndex( pos ) ] = doorCost;
        }
    }

    public override bool UpdateIncrementally(IEnumerable<PathRequest> _, List<IntVec3> cellDeltas)
    {
        CellIndices cellIndices = map.cellIndices;
        Building[] buildingArray = map.edificeGrid.InnerArray;
        foreach( IntVec3 cellDelta in cellDeltas )
        {
            int num = cellIndices.CellToIndex( cellDelta );
            if( buildingArray[ num ] is Building_Door door )
                costGrid[ num ] = GetDoorCost( door );
            else
                costGrid[ num ] = 0;
        }
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static ushort GetDoorCost( Building_Door door )
    {
        return DoorPriorityInfo.GetNoCreate( door ).DoorPriority switch
        {
            DoorPriority.Normal => 0,
            DoorPriority.Side => (ushort) PathfindingAvoidanceMod.settings.sideDoorCost,
            DoorPriority.Emergency => (ushort) PathfindingAvoidanceMod.settings.emergencyDoorCost,
            _ => throw new ArgumentOutOfRangeException()
        };
    }
}
