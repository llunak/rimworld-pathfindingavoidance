using HarmonyLib;
using Verse;
using System.Reflection;

namespace PathfindingAvoidance;

[StaticConstructorOnStartup]
public class HarmonyPatches
{
    static HarmonyPatches()
    {
        var harmony = new Harmony("llunak.PathfindingAvoidance");
        harmony.PatchAll(Assembly.GetExecutingAssembly());
    }
}
