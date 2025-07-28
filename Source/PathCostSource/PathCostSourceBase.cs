using Verse;
using System;
using System.Collections.Generic;
using Unity.Collections;

namespace PathfindingAvoidance;

// NOTE: This is called from parallel threads, and so must be thread-safe, including the code it calls.
public abstract class PathCostSourceBase : IPathFinderDataSource, IDisposable
{
    protected readonly Map map;
    // The extra costs (indexed by something like 'map.cellIndices.CellToIndex( cell )').
    protected NativeArray<ushort> costGrid;
    // Set from UpdateIncrementally(), do not include cells from cellDeltas.
    protected HashSet<IntVec3> extraChangedCells = [];

    public NativeArray<ushort> CostGrid => costGrid;
    public HashSet<IntVec3> ExtraChangedCells => extraChangedCells;

    public PathCostSourceBase(Map map)
    {
        this.map = map;
        int numGridCells = map.cellIndices.NumGridCells;
        costGrid = new NativeArray<ushort>(numGridCells, Allocator.Persistent);
    }

    public virtual void Dispose()
    {
        costGrid.Dispose();
    }

    public void ResetExtraChangedCells()
    {
        extraChangedCells.Clear();
    }

    // IPathFinderDataSource:
    public abstract void ComputeAll(IEnumerable<PathRequest> _);
    // The meaning of the return value seems to be: Return true if a cell not in cellDeltas
    // has also been modified.
    public abstract bool UpdateIncrementally(IEnumerable<PathRequest> _, List<IntVec3> cellDeltas);
}
