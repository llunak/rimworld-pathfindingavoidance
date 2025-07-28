using Verse;
using System;
using System.Collections.Generic;

namespace PathfindingAvoidance;

// Responsible for creating and maintaining PathCostSourceBase objects.
public class PathCostSourceHandler
{
    private static Dictionary< PathFinderMapData, PathCostSourceHandler > instancesMap = [];

    private readonly PathFinderMapData mapData;

    private Dictionary< PathType, List< PathCostSourceBase >> sources = [];

    public static PathCostSourceHandler Get( PathFinderMapData mapData )
    {
        PathCostSourceHandler handler;
        if( instancesMap.TryGetValue( mapData, out handler ))
            return handler;
        handler = new PathCostSourceHandler( mapData );
        instancesMap[ mapData ] = handler;
        return handler;
    }

    public static void RemoveMap( PathFinderMapData mapData )
    {
        instancesMap.Remove( mapData );
    }

    public PathCostSourceHandler( PathFinderMapData mapData )
    {
        this.mapData = mapData;
        CreateSources();
    }

// TODO Dispose
// TODO Dispose+Recreate on config changes

    public void CreateSources()
    {
        Map map = mapData.map;
        foreach( PathType pathType in Enum.GetValues( typeof( PathType )))
            sources[ pathType ] = [];
        if( TerrainFilthCostSource.IsEnabled())
            CreateSource( new TerrainFilthCostSource( map ), [ PathType.Colony, PathType.Friendly ] );
        if( DoorCostSource.IsEnabled())
            CreateSource( new DoorCostSource( map ), [ PathType.Colony, PathType.Friendly ] );
        if( ZoneCostSource.IsEnabled( PathType.Colony ))
            CreateSource( new ZoneCostSource( map, PathType.Colony ), [ PathType.Colony ] );
        if( ZoneCostSource.IsEnabled( PathType.Friendly ))
            CreateSource( new ZoneCostSource( map, PathType.Friendly ), [ PathType.Friendly ] );
        if( FriendlyRoomCostSource.IsEnabled())
            CreateSource( new FriendlyRoomCostSource( map ), [ PathType.Friendly ] );
    }

    private void CreateSource( PathCostSourceBase source, PathType[] pathTypes )
    {
        mapData.RegisterSource( source );
        foreach( PathType pathType in pathTypes )
            sources[ pathType ].Add( source );
    }

    public IEnumerable< PathCostSourceBase > GetSources( PathType pathType )
    {
        return sources[ pathType ];
    }
}
