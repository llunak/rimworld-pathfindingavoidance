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
    // Change to other PathType to enable drawing of costs.
    private const PathType DEBUG_TYPE = PathType.None;

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
        ComputeAllFilth();
        ComputeAllDoors();
        ComputeAllRooms();
        ComputeAllDebug();
        ++lastUpdateId;
    }

    public bool UpdateIncrementally(IEnumerable<PathRequest> _, List<IntVec3> cellDeltas)
    {
        CellIndices cellIndices = map.cellIndices;
        foreach( IntVec3 cellDelta in cellDeltas )
            cost[ cellIndices.CellToIndex( cellDelta ) ] = 0;
        UpdateIncrementallyFilth( cellDeltas );
        UpdateIncrementallyDoors( cellDeltas );
        UpdateIncrementallyRooms( cellDeltas );
        UpdateIncrementallyDebug( cellDeltas );
        if( cellDeltas.Count != 0 )
            ++lastUpdateId;
        return false;
    }

    // Avoid cells with terrain that creates filth.
    private void ComputeAllFilth()
    {
        if( IsEnabledFilth())
        {
            TerrainGrid terrainGrid = map.terrainGrid;
            for( int i = 0; i < map.cellIndices.NumGridCells; ++i )
            {
                TerrainDef terrainDef = terrainGrid.TerrainAt( i );
                if( terrainDef != null )
                    cost[i] += GetTerrainCost( terrainDef );
            }
        }
    }

    private void UpdateIncrementallyFilth( List<IntVec3> cellDeltas )
    {
        if( IsEnabledFilth())
        {
            CellIndices cellIndices = map.cellIndices;
            TerrainGrid terrainGrid = map.terrainGrid;
            foreach( IntVec3 cellDelta in cellDeltas )
            {
                int num = cellIndices.CellToIndex( cellDelta );
                TerrainDef terrainDef = terrainGrid.TerrainAt( num );
                if (terrainDef != null)
                    cost[ num ] += GetTerrainCost( terrainDef );
            }
        }
    }

    // Avoid doors with configured priority.
    private void ComputeAllDoors()
    {
        if( IsEnabledDoors())
        {
            CellIndices cellIndices = map.cellIndices;
            foreach( Building_Door door in map.listerBuildings.AllBuildingsColonistOfClass< Building_Door >())
            {
                ushort doorCost = GetDoorCost( door );
                if( doorCost > 0 )
                    foreach( IntVec3 pos in door.OccupiedRect().Cells )
                        cost[ cellIndices.CellToIndex( pos ) ] += doorCost;
            }
        }
    }

    private void UpdateIncrementallyDoors( List<IntVec3> cellDeltas )
    {
        if( IsEnabledDoors())
        {
            CellIndices cellIndices = map.cellIndices;
            Building[] buildingArray = map.edificeGrid.InnerArray;
            foreach( IntVec3 cellDelta in cellDeltas )
            {
                int num = cellIndices.CellToIndex( cellDelta );
                if( buildingArray[ num ] is Building_Door door )
                    cost[ num ] += GetDoorCost( door );
            }
        }
    }

    // Make friendly visits avoid walking through rooms.
    private void ComputeAllRooms()
    {
        if( pathType == PathType.Friendly && IsEnabledRooms())
        {
            CellIndices cellIndices = map.cellIndices;
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
    }

    private void UpdateIncrementallyRooms( List<IntVec3> cellDeltas )
    {
        if( pathType == PathType.Friendly && IsEnabledRooms())
        {
            CellIndices cellIndices = map.cellIndices;
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
    }

    private void ComputeAllDebug()
    {
        if( pathType == DEBUG_TYPE )
        {
            CellIndices cellIndices = map.cellIndices;
            map.debugDrawer.debugCells.Clear(); // FlashCell() adds unconditionally, so remove old, they'll be overwritten
            for( int i = 0; i < map.cellIndices.NumGridCells; ++i )
            {
                IntVec3 cell = cellIndices.IndexToCell( i );
                // TODO use a better mapping for the cost range
                map.debugDrawer.FlashCell( cell, cost[ i ] / 2000f, cost[ i ].ToString());
            }
        }
    }

    private void UpdateIncrementallyDebug( List<IntVec3> cellDeltas )
    {
        if( pathType == DEBUG_TYPE )
        {
            CellIndices cellIndices = map.cellIndices;
            foreach( IntVec3 cellDelta in cellDeltas )
            {
                int num = cellIndices.CellToIndex( cellDelta );
                map.debugDrawer.debugCells.RemoveAll( ( DebugCell c ) => c.c == cellDelta );
                map.debugDrawer.FlashCell( cellDelta, cost[ num ] / 2000f, cost[ num ].ToString());
            }
        }
    }

    private static bool IsEnabledFilth()
    {
        return PathfindingAvoidanceMod.settings.dirtyCost != 0;
    }

    private static ushort GetTerrainCost( TerrainDef terrainDef )
    {
        ushort cost = 0;
        if( terrainDef.generatedFilth != null )
            cost += (ushort) PathfindingAvoidanceMod.settings.dirtyCost;
        return cost;
    }

    private static bool IsEnabledDoors()
    {
        return PathfindingAvoidanceMod.settings.sideDoorCost != 0
            || PathfindingAvoidanceMod.settings.emergencyDoorCost != 0;
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

    private static bool IsEnabledRooms()
    {
        return PathfindingAvoidanceMod.settings.visitingCaravanOutdoorsRoomCost != 0
            || PathfindingAvoidanceMod.settings.visitingCaravanIndoorRoomCost != 0;
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
