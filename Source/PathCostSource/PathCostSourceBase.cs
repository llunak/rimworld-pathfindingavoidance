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
    // Set cells that need change (in additional to cells from cellDeltas).
    protected HashSet<IntVec3> extraChangedCells = [];
    // Set if all cells changed, not necessary to call if ComputeAll() is called (by RimWorld code).
    protected bool allChanged = false;

    public NativeArray<ushort> CostGrid => costGrid;
    public HashSet<IntVec3> ExtraChangedCells => extraChangedCells;
    public bool AllChanged => allChanged;

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

    // Will be called after every call to ComputeAll() or UpdateIncrementally().
    public void ResetChanged()
    {
        extraChangedCells.Clear();
        allChanged = false;
    }

    // IPathFinderDataSource:
    public abstract void ComputeAll(IEnumerable<PathRequest> _);
    // The meaning of the return value seems to be: Return true if a cell not in cellDeltas
    // has also been modified.
    public abstract bool UpdateIncrementally(IEnumerable<PathRequest> _, List<IntVec3> cellDeltas);
}
