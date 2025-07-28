using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using LudeonTK;
using HarmonyLib;

namespace PathfindingAvoidance;

// Avoid growing zones.
public class ZoneCostSource : PathCostSourceBase
{
    private readonly PathType pathType;
    private static List< ZoneCostSource > sources = []; // There won't be that many maps, List is fine.
    private List< IntVec3 > pendingCells = [];

    public ZoneCostSource(Map map, PathType pathType)
        : base( map )
    {
        this.pathType = pathType;
        sources.Add( this );
    }

    public override void Dispose()
    {
        base.Dispose();
        sources.Remove( this );
    }

    public static bool IsEnabled( PathType pathType )
    {
        return PathfindingAvoidanceMod.settings.growingZoneCost[ (int)pathType ] != 0;
    }

    public override void ComputeAll(IEnumerable<PathRequest> _)
    {
        costGrid.Clear();
        pendingCells.Clear();
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
        var updateCell = ( IntVec3 cell ) =>
        {
            int num = cellIndices.CellToIndex( cell );
            Zone zone = map.zoneManager.ZoneAt( cell );
            ushort zoneCost = GetZoneCost( zone );
            costGrid[ num ] = zoneCost;
        };
        foreach( IntVec3 cellDelta in cellDeltas )
            updateCell( cellDelta );
        foreach( IntVec3 cell in pendingCells )
        {
            updateCell( cell );
            extraChangedCells.Add( cell );
        }
        bool result = pendingCells.Count != 0;
        pendingCells.Clear();
        return result;
    }

    private ushort GetZoneCost( Zone zone )
    {
        if( !( zone is Zone_Growing ))
            return 0;
        return (ushort) PathfindingAvoidanceMod.settings.growingZoneCost[ (int)pathType ];
    }

    public static void ZoneCellChanged( Zone zone, IntVec3 cell )
    {
        if( !( zone is Zone_Growing ))
            return;
        foreach( ZoneCostSource source in sources )
            if( source.map == zone.Map )
                source.pendingCells.Add( cell );
    }
}

// Update if a zone changes.
[HarmonyPatch(typeof(Zone))]
public static class Zone_Patch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(AddCell))]
    public static void AddCell(Zone __instance, IntVec3 c)
    {
        ZoneCostSource.ZoneCellChanged( __instance, c );
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(RemoveCell))]
    public static void RemoveCell(Zone __instance, IntVec3 c)
    {
        ZoneCostSource.ZoneCellChanged( __instance, c );
    }
}
