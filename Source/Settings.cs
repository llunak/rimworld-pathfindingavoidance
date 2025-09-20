using Verse;
using UnityEngine;

namespace PathfindingAvoidance;

public class Settings : ModSettings
{
    public const int DIRTY_COST = 10;
    public const int WEATHER_COST = 20;
    public const int SIDE_DOOR_COST = 200;
    public const int EMERGENCY_DOOR_COST = 500;
    public const int AREA_AVOID_LOW_COST = 10;
    public const int AREA_AVOID_MEDIUM_COST = 50;
    public const int AREA_AVOID_HIGH_COST = 500;
    public const int VISITING_CARAVAN_OUTDOORS_ROOM_COST = 10;
    public const int VISITING_CARAVAN_INDOOR_ROOM_COST = 100;
    public const int GROWING_ZONE_COST_COLONY = 10;
    public const int GROWING_ZONE_COST_FRIENDLY = 10;

    public int dirtyCost = DIRTY_COST;
    public int weatherCost = WEATHER_COST;
    public int sideDoorCost = SIDE_DOOR_COST;
    public int emergencyDoorCost = EMERGENCY_DOOR_COST;
    public int areaAvoidLowCost = AREA_AVOID_LOW_COST;
    public int areaAvoidMediumCost = AREA_AVOID_MEDIUM_COST;
    public int areaAvoidHighCost = AREA_AVOID_HIGH_COST;
    public int visitingCaravanOutdoorsRoomCost = VISITING_CARAVAN_OUTDOORS_ROOM_COST;
    public int visitingCaravanIndoorRoomCost = VISITING_CARAVAN_INDOOR_ROOM_COST;
    public int[] growingZoneCost = new int[] { 0, GROWING_ZONE_COST_COLONY, GROWING_ZONE_COST_FRIENDLY };

    public override void ExposeData()
    {
        Scribe_Values.Look( ref dirtyCost, "DirtyCost", DIRTY_COST );
        Scribe_Values.Look( ref weatherCost, "WeatherCost", WEATHER_COST );
        Scribe_Values.Look( ref sideDoorCost, "SideDoorCost", SIDE_DOOR_COST );
        Scribe_Values.Look( ref emergencyDoorCost, "EmergencyDoorCost", EMERGENCY_DOOR_COST );
        Scribe_Values.Look( ref areaAvoidLowCost, "AreaAvoidLowCost", AREA_AVOID_LOW_COST );
        Scribe_Values.Look( ref areaAvoidMediumCost, "AreaAvoidMediumCost", AREA_AVOID_MEDIUM_COST );
        Scribe_Values.Look( ref areaAvoidHighCost, "AreaAvoidHighCost", AREA_AVOID_HIGH_COST );
        Scribe_Values.Look( ref visitingCaravanOutdoorsRoomCost, "VisitingCaravanOutdoorsRoomCost", VISITING_CARAVAN_OUTDOORS_ROOM_COST );
        Scribe_Values.Look( ref visitingCaravanIndoorRoomCost, "VisitingCaravanIndoorRoomCost", VISITING_CARAVAN_INDOOR_ROOM_COST );
        Scribe_Values.Look( ref growingZoneCost[ (int)PathType.Colony ], "GrowingZoneCostColony", GROWING_ZONE_COST_COLONY );
        Scribe_Values.Look( ref growingZoneCost[ (int)PathType.Friendly ], "GrowingZoneCostFriendly", GROWING_ZONE_COST_FRIENDLY );
    }

    public bool IsEnabled(PathType pathType)
    {
        // Return true if the PathType has any effect (any of its features
        // adds a cost, see PathCostSource).
        switch( pathType )
        {
            case PathType.Colony:
                return dirtyCost != 0 || weatherCost != 0 || sideDoorCost != 0 || emergencyDoorCost != 0
                    || growingZoneCost[ (int)pathType ] != 0
                    || areaAvoidLowCost != 0
                    || areaAvoidMediumCost != 0
                    || areaAvoidHighCost != 0;
            case PathType.Friendly:
                return dirtyCost != 0 || weatherCost != 0 || sideDoorCost != 0 || emergencyDoorCost != 0
                    || visitingCaravanOutdoorsRoomCost != 0
                    || visitingCaravanIndoorRoomCost != 0
                    || growingZoneCost[ (int)pathType ] != 0
                    || areaAvoidLowCost != 0
                    || areaAvoidMediumCost != 0
                    || areaAvoidHighCost != 0;
            case PathType.None:
            default:
                return false;
        }
    }
}

public class PathfindingAvoidanceMod : Mod
{
    private static Settings _settings;
    public static Settings settings { get { return _settings; }}

    public PathfindingAvoidanceMod( ModContentPack content )
        : base( content )
    {
        _settings = GetSettings< Settings >();
    }

    public override string SettingsCategory()
    {
        return "PathfindingAvoidance.ModName".Translate();
    }

    public override void DoSettingsWindowContents(Rect rect)
    {
        Listing_Standard listing = new Listing_Standard();
        listing.Begin( rect );
        listing.Label( "PathfindingAvoidance.GeneralCosts".Translate(), tooltip : "PathfindingAvoidance.GeneralCostsTooltip".Translate());
        listing.GapLine();
        settings.dirtyCost = (int) listing.SliderLabeled( "PathfindingAvoidance.DirtyCost".Translate( settings.dirtyCost ),
            settings.dirtyCost, 0, 100, tooltip : "PathfindingAvoidance.DirtyCostTooltip".Translate()
                + "\n\n" + "PathfindingAvoidance.ExtraCostTooltip".Translate( Settings.DIRTY_COST ));
        settings.weatherCost = (int) listing.SliderLabeled( "PathfindingAvoidance.WeatherCost".Translate( settings.weatherCost ),
            settings.weatherCost, 0, 100, tooltip : "PathfindingAvoidance.WeatherCostTooltip".Translate()
                + "\n\n" + "PathfindingAvoidance.ExtraCostTooltip".Translate( Settings.WEATHER_COST ));
        settings.sideDoorCost = (int) listing.SliderLabeled( "PathfindingAvoidance.SideDoorCost".Translate( settings.sideDoorCost ),
            settings.sideDoorCost, 50, 500, tooltip : "PathfindingAvoidance.SideDoorCostTooltip".Translate()
                + "\n\n" + "PathfindingAvoidance.ExtraCostTooltip".Translate( Settings.SIDE_DOOR_COST ));
        settings.emergencyDoorCost = (int) listing.SliderLabeled( "PathfindingAvoidance.EmergencyDoorCost".Translate( settings.emergencyDoorCost ),
            settings.emergencyDoorCost, 200, 1000, tooltip : "PathfindingAvoidance.EmergencyDoorCostTooltip".Translate()
                + "\n\n" + "PathfindingAvoidance.ExtraCostTooltip".Translate( Settings.EMERGENCY_DOOR_COST ));
        settings.areaAvoidLowCost = (int) listing.SliderLabeled( "PathfindingAvoidance.AreaAvoidLowCost".Translate( settings.areaAvoidLowCost ),
            settings.areaAvoidLowCost, 0, 100, tooltip : "PathfindingAvoidance.AreaAvoidLowCostTooltip".Translate()
                + "\n\n" + "PathfindingAvoidance.ExtraCostTooltip".Translate( Settings.AREA_AVOID_LOW_COST ));
        settings.areaAvoidMediumCost = (int) listing.SliderLabeled( "PathfindingAvoidance.AreaAvoidMediumCost".Translate( settings.areaAvoidMediumCost ),
            settings.areaAvoidMediumCost, 0, 100, tooltip : "PathfindingAvoidance.AreaAvoidMediumCostTooltip".Translate()
                + "\n\n" + "PathfindingAvoidance.ExtraCostTooltip".Translate( Settings.AREA_AVOID_MEDIUM_COST ));
        settings.areaAvoidHighCost = (int) listing.SliderLabeled( "PathfindingAvoidance.AreaAvoidHighCost".Translate( settings.areaAvoidHighCost ),
            settings.areaAvoidHighCost, 0, 100, tooltip : "PathfindingAvoidance.AreaAvoidHighCostTooltip".Translate()
                + "\n\n" + "PathfindingAvoidance.ExtraCostTooltip".Translate( Settings.AREA_AVOID_HIGH_COST ));

        listing.Gap();
        listing.Label( "PathfindingAvoidance.ColonyCosts".Translate(), tooltip : "PathfindingAvoidance.ColonyCostsTooltip".Translate());
        listing.GapLine();
        settings.growingZoneCost[ (int)PathType.Colony ] = (int) listing.SliderLabeled( "PathfindingAvoidance.GrowingZoneCost"
            .Translate( settings.growingZoneCost[ (int)PathType.Colony ] ), settings.growingZoneCost[ (int)PathType.Colony ],
            0, 100, tooltip : "PathfindingAvoidance.GrowingZoneCostTooltip".Translate()
                + "\n\n" + "PathfindingAvoidance.ExtraCostTooltip".Translate( Settings.GROWING_ZONE_COST_COLONY ));

        listing.Gap();
        listing.Label( "PathfindingAvoidance.FriendlyCosts".Translate(), tooltip : "PathfindingAvoidance.FriendlyCostsTooltip".Translate());
        listing.GapLine();
        settings.visitingCaravanOutdoorsRoomCost = (int) listing.SliderLabeled(
            "PathfindingAvoidance.VisitingCaravanOutdoorsRoomCost".Translate( settings.visitingCaravanOutdoorsRoomCost ),
            settings.visitingCaravanOutdoorsRoomCost, 0, 500, tooltip : "PathfindingAvoidance.VisitingCaravanOutdoorsRoomCostTooltip".Translate()
                + "\n\n" + "PathfindingAvoidance.ExtraCostTooltip".Translate( Settings.VISITING_CARAVAN_OUTDOORS_ROOM_COST ));
        settings.visitingCaravanIndoorRoomCost = (int) listing.SliderLabeled(
            "PathfindingAvoidance.VisitingCaravanIndoorRoomCost".Translate( settings.visitingCaravanIndoorRoomCost ),
            settings.visitingCaravanIndoorRoomCost, 0, 500, tooltip : "PathfindingAvoidance.VisitingCaravanIndoorRoomCostTooltip".Translate()
                + "\n\n" + "PathfindingAvoidance.ExtraCostTooltip".Translate( Settings.VISITING_CARAVAN_INDOOR_ROOM_COST ));
        settings.growingZoneCost[ (int)PathType.Friendly ] = (int) listing.SliderLabeled( "PathfindingAvoidance.GrowingZoneCost"
            .Translate( settings.growingZoneCost[ (int)PathType.Friendly ] ), settings.growingZoneCost[ (int)PathType.Friendly ],
            0, 100, tooltip : "PathfindingAvoidance.GrowingZoneCostTooltip".Translate()
                + "\n\n" + "PathfindingAvoidance.ExtraCostTooltip".Translate( Settings.GROWING_ZONE_COST_FRIENDLY ));
        listing.End();
        base.DoSettingsWindowContents(rect);
    }

    public override void WriteSettings()
    {
        base.WriteSettings();
        if( Current.Game != null && Current.Game.Maps != null )
            foreach( Map map in Current.Game.Maps )
                map.pathFinder.MapData.Notify_MapDirtied();
    }
}
