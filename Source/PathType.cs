namespace PathfindingAvoidance;

// Specifies which kind of path costs should be applied.
public enum PathType
{
    None, // Not any of the types below.
    Colony, // Colony pawns (except for animals and exclusions).
    Friendly, // Visiting caravans, etc.
    COUNT
}

public static class PathTypeUtils
{
    public static bool IsEnabled(this PathType pathType)
    {
        return PathfindingAvoidanceMod.settings.IsEnabled( pathType );
    }
}
