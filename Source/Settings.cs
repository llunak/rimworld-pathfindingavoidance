using Verse;
using UnityEngine;

namespace PathfindingAvoidance;

public class Settings : ModSettings
{
    public int dirtyCost = 10;
    public int sideDoorCost = 200;
    public int emergencyDoorCost = 500;
    public int visitingCaravanOutdoorsRoomCost = 10;
    public int visitingCaravanIndoorRoomCost = 100;

    public override void ExposeData()
    {
        Scribe_Values.Look( ref dirtyCost, "DirtyCost", 10 );
        Scribe_Values.Look( ref sideDoorCost, "SideDoorCost", 200 );
        Scribe_Values.Look( ref emergencyDoorCost, "EmergencyDoorCost", 500 );
        Scribe_Values.Look( ref visitingCaravanOutdoorsRoomCost, "VisitingCaravanOutdoorsRoomCost", 10 );
        Scribe_Values.Look( ref visitingCaravanIndoorRoomCost, "VisitingCaravanIndoorRoomCost", 100 );
    }

    public bool IsEnabled(PathType pathType)
    {
        // Return true if the PathType has any effect (any of its features
        // adds a cost, see PathCostSource).
        switch( pathType )
        {
            case PathType.Colony:
                return dirtyCost != 0 || sideDoorCost != 0 || emergencyDoorCost != 0;
            case PathType.Friendly:
                return dirtyCost != 0 || sideDoorCost != 0 || emergencyDoorCost != 0
                    || visitingCaravanOutdoorsRoomCost != 0
                    || visitingCaravanIndoorRoomCost != 0;
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
        listing.Label( "PathfindingAvoidance.FriendlyCosts".Translate(), tooltip : "PathfindingAvoidance.FriendlyCostsTooltip".Translate());
        listing.GapLine();
        settings.visitingCaravanOutdoorsRoomCost = (int) listing.SliderLabeled(
            "PathfindingAvoidance.VisitingCaravanOutdoorsRoomCost".Translate( settings.visitingCaravanOutdoorsRoomCost ),
            settings.visitingCaravanOutdoorsRoomCost, 0, 500, tooltip : "PathfindingAvoidance.VisitingCaravanOutdoorsRoomCostTooltip".Translate());
        settings.visitingCaravanIndoorRoomCost = (int) listing.SliderLabeled(
            "PathfindingAvoidance.VisitingCaravanIndoorRoomCost".Translate( settings.visitingCaravanIndoorRoomCost ),
            settings.visitingCaravanIndoorRoomCost, 0, 500, tooltip : "PathfindingAvoidance.VisitingCaravanIndoorRoomCostTooltip".Translate());
        listing.End();
        base.DoSettingsWindowContents(rect);

        if( Current.Game != null && Current.Game.Maps != null )
            foreach( Map map in Current.Game.Maps )
                map.pathFinder.MapData.Notify_MapDirtied();
    }
}
