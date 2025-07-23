using HarmonyLib;
using RimWorld;
using Verse;
using UnityEngine;
using System;
using System.Collections.Generic;

namespace PathfindingAvoidance;

// Add a gizmo that allows to set lower priority for door, resulting in pawns
// trying to avoid that door.

public enum DoorPriority
{
    Normal,
    Side,
    Emergency
};

public class DoorPriorityInfo
{
    private DoorPriority doorPriority = DoorPriority.Normal;
    public DoorPriority DoorPriority => doorPriority;

    private Building_Door building;

    private static Dictionary< Building_Door, DoorPriorityInfo > dict = new Dictionary< Building_Door, DoorPriorityInfo >();

    public static DoorPriorityInfo Get( Building_Door door )
    {
        DoorPriorityInfo info;
        if( dict.TryGetValue( door, out info ))
            return info;
        info = new DoorPriorityInfo();
        info.building = door;
        dict[ door ] = info;
        return info;
    }

    public static DoorPriorityInfo GetNoCreate( Building_Door door )
    {
        return dict[ door ];
    }

    public static void Remove( Building_Door door )
    {
        dict.Remove( door );
    }

    public static void RemoveAll()
    {
        dict.Clear();
    }

    public void SwitchNextPriority()
    {
        doorPriority = doorPriority switch
        {
            DoorPriority.Normal => DoorPriority.Side,
            DoorPriority.Side => DoorPriority.Emergency,
            DoorPriority.Emergency => DoorPriority.Normal,
            _ => throw new ArgumentOutOfRangeException()
        };
        building.Map.pathFinder.mapData.Notify_BuildingChanged( building );
    }

    public void ExposeData()
    {
        Scribe_Values.Look( ref doorPriority, "PathfindingAvoidance.DoorPriority", DoorPriority.Normal );
    }
};

[HarmonyPatch(typeof(Building_Door))]
public static class Building_Door_Patch
{
    [HarmonyPostfix]
    [HarmonyPatch(nameof(ExposeData))]
    public static void ExposeData( Building_Door __instance )
    {
        DoorPriorityInfo.Get( __instance ).ExposeData();
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(SpawnSetup))]
    public static void SpawnSetup( Building_Door __instance )
    {
        DoorPriorityInfo.Get( __instance ); // Create the info if needed.
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(DeSpawn))]
    public static void DeSpawn( Building_Door __instance )
    {
        DoorPriorityInfo.Remove( __instance );
    }

    [HarmonyPostfix]
    [HarmonyPatch(nameof(GetGizmos))]
    public static IEnumerable< Gizmo > GetGizmos( IEnumerable< Gizmo > __result, Building_Door __instance )
    {
        foreach( Gizmo gizmo in __result )
            yield return gizmo;
        if( __instance.Faction != Faction.OfPlayer )
            yield break;
        DoorPriorityInfo info = DoorPriorityInfo.Get( __instance );
        Command_Action action = new Command_Action();
        action.defaultLabel = ( "PathfindingAvoidance.DoorPriorityLabel" + info.DoorPriority.ToString()).Translate();
        action.defaultDesc = "PathfindingAvoidance.DoorPriorityDesc".Translate();
        action.icon = ContentFinder<Texture2D>.Get("UI/Designators/PathAvoidance_DoorPriority");
        action.hotKey = KeyBindingDefOf.Misc12; // 'P' by default
        action.activateSound = SoundDefOf.Checkbox_TurnedOn;
        action.action = () => info.SwitchNextPriority();
        yield return action;
    }
}

[HarmonyPatch(typeof(Game))]
public static class Game_Patch
{
    [HarmonyPrefix]
    [HarmonyPatch(nameof(InitNewGame))]
    public static void InitNewGame()
    {
        DoorPriorityInfo.RemoveAll();
    }

    [HarmonyPrefix]
    [HarmonyPatch(nameof(LoadGame))]
    public static void LoadGame()
    {
        DoorPriorityInfo.RemoveAll();
    }
}
