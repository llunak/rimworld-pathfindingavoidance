using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using LudeonTK;
using HarmonyLib;
using System.Runtime.CompilerServices;

namespace PathfindingAvoidance;

// Avoid growing zones.
public class ZoneCostSource : PathCostSourceBase
{
    private readonly PathType pathType;
    private static List< ZoneCostSource > sources = []; // There won't be that many maps, List is fine.

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
        Trace.Log("Updating all cells for ZoneCostSource, map: " + map);
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
        Trace.Log("Updating " + cellDeltas.Count + "+" + extraChangedCells.Count + " cells for ZoneCostSource, map: " + map);
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
        foreach( IntVec3 cell in extraChangedCells )
            updateCell( cell );
        bool result = extraChangedCells.Count != 0;
        return result;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
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
                source.extraChangedCells.Add( cell );
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
