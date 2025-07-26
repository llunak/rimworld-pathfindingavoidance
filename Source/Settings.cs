using Verse;
using UnityEngine;

namespace PathfindingAvoidance;

public class Settings : ModSettings
{
    private const int DIRTY_COST = 10;
    private const int SIDE_DOOR_COST = 200;
    private const int EMERGENCY_DOOR_COST = 500;
    private const int VISITING_CARAVAN_OUTDOORS_ROOM_COST = 10;
    private const int VISITING_CARAVAN_INDOOR_ROOM_COST = 100;
    private const int GROWING_ZONE_COST_COLONY = 10;
    private const int GROWING_ZONE_COST_FRIENDLY = 10;

    public int dirtyCost = DIRTY_COST;
    public int sideDoorCost = SIDE_DOOR_COST;
    public int emergencyDoorCost = EMERGENCY_DOOR_COST;
    public int visitingCaravanOutdoorsRoomCost = VISITING_CARAVAN_OUTDOORS_ROOM_COST;
    public int visitingCaravanIndoorRoomCost = VISITING_CARAVAN_INDOOR_ROOM_COST;
    public int[] growingZoneCost = new int[] { 0, GROWING_ZONE_COST_COLONY, GROWING_ZONE_COST_FRIENDLY };

    public override void ExposeData()
    {
        Scribe_Values.Look( ref dirtyCost, "DirtyCost", DIRTY_COST );
        Scribe_Values.Look( ref sideDoorCost, "SideDoorCost", SIDE_DOOR_COST );
        Scribe_Values.Look( ref emergencyDoorCost, "EmergencyDoorCost", EMERGENCY_DOOR_COST );
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
                return dirtyCost != 0 || sideDoorCost != 0 || emergencyDoorCost != 0
                    || growingZoneCost[ (int)pathType ] != 0;
            case PathType.Friendly:
                return dirtyCost != 0 || sideDoorCost != 0 || emergencyDoorCost != 0
                    || visitingCaravanOutdoorsRoomCost != 0
                    || visitingCaravanIndoorRoomCost != 0
                    || growingZoneCost[ (int)pathType ] != 0;
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
            settings.dirtyCost, 0, 100, tooltip : "PathfindingAvoidance.DirtyCostTooltip".Translate());
        settings.sideDoorCost = (int) listing.SliderLabeled( "PathfindingAvoidance.SideDoorCost".Translate( settings.sideDoorCost ),
            settings.sideDoorCost, 50, 500, tooltip : "PathfindingAvoidance.SideDoorCostTooltip".Translate());
        settings.emergencyDoorCost = (int) listing.SliderLabeled( "PathfindingAvoidance.EmergencyDoorCost".Translate( settings.emergencyDoorCost ),
            settings.emergencyDoorCost, 200, 1000, tooltip : "PathfindingAvoidance.EmergencyDoorCostTooltip".Translate());

        listing.Gap();
        listing.Label( "PathfindingAvoidance.ColonyCosts".Translate(), tooltip : "PathfindingAvoidance.ColonyCostsTooltip".Translate());
        listing.GapLine();
        settings.growingZoneCost[ (int)PathType.Colony ] = (int) listing.SliderLabeled( "PathfindingAvoidance.GrowingZoneCost"
            .Translate( settings.growingZoneCost[ (int)PathType.Colony ] ), settings.growingZoneCost[ (int)PathType.Colony ],
            0, 100, tooltip : "PathfindingAvoidance.GrowingZoneCostTooltip".Translate());

        listing.Gap();
        listing.Label( "PathfindingAvoidance.FriendlyCosts".Translate(), tooltip : "PathfindingAvoidance.FriendlyCostsTooltip".Translate());
        listing.GapLine();
        settings.visitingCaravanOutdoorsRoomCost = (int) listing.SliderLabeled(
            "PathfindingAvoidance.VisitingCaravanOutdoorsRoomCost".Translate( settings.visitingCaravanOutdoorsRoomCost ),
            settings.visitingCaravanOutdoorsRoomCost, 0, 500, tooltip : "PathfindingAvoidance.VisitingCaravanOutdoorsRoomCostTooltip".Translate());
        settings.visitingCaravanIndoorRoomCost = (int) listing.SliderLabeled(
            "PathfindingAvoidance.VisitingCaravanIndoorRoomCost".Translate( settings.visitingCaravanIndoorRoomCost ),
            settings.visitingCaravanIndoorRoomCost, 0, 500, tooltip : "PathfindingAvoidance.VisitingCaravanIndoorRoomCostTooltip".Translate());
        settings.growingZoneCost[ (int)PathType.Friendly ] = (int) listing.SliderLabeled( "PathfindingAvoidance.GrowingZoneCost"
            .Translate( settings.growingZoneCost[ (int)PathType.Friendly ] ), settings.growingZoneCost[ (int)PathType.Friendly ],
            0, 100, tooltip : "PathfindingAvoidance.GrowingZoneCostTooltip".Translate());
        listing.End();
        base.DoSettingsWindowContents(rect);

        if( Current.Game != null && Current.Game.Maps != null )
            foreach( Map map in Current.Game.Maps )
                map.pathFinder.MapData.Notify_MapDirtied();
    }
}
