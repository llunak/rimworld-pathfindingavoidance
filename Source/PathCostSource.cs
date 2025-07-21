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
    private readonly PathType pathType;

    private NativeArray<ushort> cost;
    private int lastUpdateId = 0;

    public NativeArray<ushort> Cost => cost;
    public int LastUpdateId => lastUpdateId;

    public PathCostSource(Map map, PathType pathType)
    {
        this.map = map;
        this.pathType = pathType;
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
        // Filth-avoidance.
        TerrainGrid terrainGrid = map.terrainGrid;
        for( int i = 0; i < map.cellIndices.NumGridCells; ++i )
        {
            TerrainDef terrainDef = terrainGrid.TerrainAt( i );
            if( terrainDef != null )
                cost[i] += CalculateTerrainCellCost( terrainDef );
        }
        // Door priority.
        CellIndices cellIndices = map.cellIndices;
        foreach( Building_Door door in map.listerBuildings.AllBuildingsColonistOfClass< Building_Door >())
        {
            ushort doorCost = GetDoorCost( door );
            if( doorCost > 0 )
                foreach( IntVec3 pos in door.OccupiedRect().Cells )
                    cost[ cellIndices.CellToIndex( pos ) ] += doorCost;
        }
        // Make friendly visits avoid walking through rooms.
        if( pathType == PathType.Friendly )
        {
            foreach( Room room in map.regionGrid.AllRooms )
            {
                ushort roomCost = GetFriendlyRoomCost( room );
                if( roomCost > 0 )
                {
                    foreach( IntVec3 pos in room.Cells )
                        cost[ cellIndices.CellToIndex( pos ) ] += roomCost;
                }
            }
        }
        ++lastUpdateId;
    }

    public bool UpdateIncrementally(IEnumerable<PathRequest> requests, List<IntVec3> cellDeltas)
    {
        CellIndices cellIndices = map.cellIndices;
        TerrainGrid terrainGrid = map.terrainGrid;
        foreach( IntVec3 cellDelta in cellDeltas )
        {
            int num = cellIndices.CellToIndex( cellDelta );
            TerrainDef terrainDef = terrainGrid.TerrainAt( num );
            if (terrainDef != null)
                cost[ num ] = CalculateTerrainCellCost( terrainDef );
            else
                cost[ num ] = 0;
        }
        Building[] buildingArray = map.edificeGrid.InnerArray;
        foreach( IntVec3 cellDelta in cellDeltas )
        {
            int num = cellIndices.CellToIndex( cellDelta );
            if( buildingArray[ num ] is Building_Door door )
                cost[ num ] += GetDoorCost( door );
        }
        if( pathType == PathType.Friendly )
        {
            foreach( IntVec3 cellDelta in cellDeltas )
            {
                Room room = cellDelta.GetRoom( map );
                if( room == null )
                    continue;
                ushort roomCost = GetFriendlyRoomCost( room );
                if( roomCost > 0 )
                    cost[ cellIndices.CellToIndex( cellDelta ) ] += roomCost;
            }
        }
        if( cellDeltas.Count != 0 )
            ++lastUpdateId;
        return false;
    }

    private static ushort CalculateTerrainCellCost( TerrainDef terrainDef )
    {
        ushort cost = 0;
        if( terrainDef.generatedFilth != null )
            cost += (ushort) PathfindingAvoidanceMod.settings.dirtyCost;
        return cost;
    }

    private static ushort GetDoorCost( Building_Door door )
    {
        return DoorPriorityInfo.Get( door ).DoorPriority switch
        {
            DoorPriority.Normal => 0,
            DoorPriority.Side => (ushort) PathfindingAvoidanceMod.settings.sideDoorCost,
            DoorPriority.Emergency => (ushort) PathfindingAvoidanceMod.settings.emergencyDoorCost,
            _ => throw new ArgumentOutOfRangeException()
        };
    }

    private static ushort GetFriendlyRoomCost( Room room )
    {
        if( room.IsHuge || room.IsDoorway )
            return 0;
        if( room.PsychologicallyOutdoors )
            return (ushort) PathfindingAvoidanceMod.settings.visitingCaravanOutdoorsRoomCost;
        return (ushort) PathfindingAvoidanceMod.settings.visitingCaravanIndoorRoomCost;
    }
}
