using RimWorld;
using Verse;
using System;
using System.Collections.Generic;
using LudeonTK;
using System.Runtime.CompilerServices;
using HarmonyLib;

namespace PathfindingAvoidance;

// Avoid cells with bad weather (that have worse movement speed or a mood debuff).
public class WeatherCostSource : PathCostSourceBase
{
    private static List< WeatherCostSource > sources = []; // There won't be that many maps, List is fine.

    private bool lastBadWeather = false;

    public WeatherCostSource(Map map)
        : base( map )
    {
        sources.Add( this );
    }

    public override void Dispose()
    {
        base.Dispose();
        sources.Remove( this );
    }

    public static bool IsEnabled()
    {
        return PathfindingAvoidanceMod.settings.weatherCost != 0;
    }

    private bool IsBadWeather()
    {
        // If transitioning, check both weathers, so that a found path reflects even the upcoming weather.
        if( map.weatherManager.TransitionLerpFactor < 1 ) // Last weather is still partially in effect.
        {
            if( map.weatherManager.lastWeather.weatherThought != null )
                return true;
        }
        if( map.weatherManager.curWeather.weatherThought != null )
            return true;
        return map.weatherManager.CurMoveSpeedMultiplier < 1;
    }

    public override void ComputeAll(IEnumerable<PathRequest> _)
    {
        Trace.Log("Updating all cells for WeatherCostSource, map: " + map);
        CellIndices cellIndices = map.cellIndices;
        if( IsBadWeather())
            for( int i = 0; i < map.cellIndices.NumGridCells; ++i )
                costGrid[ i ] = GetWeatherCost( cellIndices.IndexToCell( i ));
        else
            costGrid.Clear();
    }

    public override bool UpdateIncrementally(IEnumerable<PathRequest> requests, List<IntVec3> cellDeltas)
    {
        if( allChanged )
        {
            ComputeAll( requests );
            return true;
        }
        Trace.Log("Updating " + cellDeltas.Count + "+" + extraChangedCells.Count + " cells for WeatherCostSource, map: " + map);
        CellIndices cellIndices = map.cellIndices;
        if( IsBadWeather())
            foreach( IntVec3 cellDelta in cellDeltas )
                costGrid[ cellIndices.CellToIndex( cellDelta ) ] = GetWeatherCost( cellDelta );
        else
            foreach( IntVec3 cellDelta in cellDeltas )
                costGrid[ cellIndices.CellToIndex( cellDelta ) ] = 0;
        return false;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private ushort GetWeatherCost( IntVec3 cell )
    {
        if( cell.Roofed( map ))
            return 0;
        return (ushort) PathfindingAvoidanceMod.settings.weatherCost;
    }

    public static void CheckUpdate( Map map )
    {
        foreach( WeatherCostSource source in sources )
            if( source.map == map )
                source.CheckUpdate();
    }

    private void CheckUpdate()
    {
        bool isBadWeather = IsBadWeather();
        if( isBadWeather == lastBadWeather )
            return;
        lastBadWeather = isBadWeather;
        allChanged = true;
    }
}

[HarmonyPatch(typeof(WeatherManager))]
public static class WeatherManager_Patch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(WeatherManager.WeatherManagerTick))]
    public static void WeatherManagerTick( WeatherManager __instance )
    {
        WeatherCostSource.CheckUpdate( __instance.map );
    }
}
