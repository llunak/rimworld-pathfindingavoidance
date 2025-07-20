using Verse;
using UnityEngine;

namespace PathfindingAvoidance;

public class Settings : ModSettings
{
    public int dirtyCost = 10;
    public int sideDoorCost = 200;
    public int emergencyDoorCost = 500;

    public override void ExposeData()
    {
        Scribe_Values.Look( ref dirtyCost, "DirtyCost", 10 );
        Scribe_Values.Look( ref sideDoorCost, "SideDoorCost", 200 );
        Scribe_Values.Look( ref emergencyDoorCost, "EmergencyDoorCost", 500 );
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
        settings.dirtyCost = (int) listing.SliderLabeled( "PathfindingAvoidance.DirtyCost".Translate( settings.dirtyCost ),
            settings.dirtyCost, 0, 100, tooltip : "PathfindingAvoidance.DirtyCostTooltip".Translate());
        settings.sideDoorCost = (int) listing.SliderLabeled( "PathfindingAvoidance.SideDoorCost".Translate( settings.sideDoorCost ),
            settings.sideDoorCost, 50, 500, tooltip : "PathfindingAvoidance.SideDoorCostTooltip".Translate());
        settings.emergencyDoorCost = (int) listing.SliderLabeled( "PathfindingAvoidance.EmergencyDoorCost".Translate( settings.emergencyDoorCost ),
            settings.emergencyDoorCost, 200, 1000, tooltip : "PathfindingAvoidance.EmergencyDoorCostTooltip".Translate());
        listing.End();
        base.DoSettingsWindowContents(rect);
        PathCostSource.RegenerateAll();
    }
}
