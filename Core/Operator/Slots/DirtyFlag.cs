using System;
using System.Runtime.CompilerServices;

namespace T3.Core.Operator.Slots;

/// <summary>
/// Manages a version-chasing synchronization where SourceVersion tracks the latest available data state and
/// ValueVersion tracks the version of the currently cached value. A slot is considered "dirty" whenever SourceVersion > ValueVersion,
/// triggering an update that recalculates the logic and synchronizes the two counters. This mechanism allows the engine to skip redundant
/// calculations by ensuring that work is only performed when a dependency has actually incremented its version.
/// </summary>
public sealed class DirtyFlag
{
    public static void IncrementGlobalTicks()
    {
        _globalTickCount += GlobalTickDiffPerFrame;
    }

    public bool IsDirty => TriggerIsEnabled || ValueVersion != SourceVersion;

    public static int InvalidationRefFrame = 0;

    public int Invalidate()
    {
        // Returns the Target - should be no performance hit according to:
        // https://stackoverflow.com/questions/12200662/are-void-methods-at-their-most-basic-faster-less-of-an-overhead-than-methods-tha

        // Debug.Assert(InvalidationRefFrame != InvalidatedWithRefFrame); // this should never happen and prevented on the calling side

        if (InvalidationRefFrame != InvalidatedWithRefFrame)
        {
            // the ref frame prevent double invalidation when outputs are connected several times
            InvalidatedWithRefFrame = InvalidationRefFrame;
            SourceVersion++;
        }
        //else
        //{
        //    Log.Error("Double invalidation of a slot. Please notify cynic about current setup.");
        //}

        return SourceVersion;
    }

    public void ForceInvalidate()
    {
        InvalidatedWithRefFrame = InvalidationRefFrame;
        SourceVersion++;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Clear()
    {
        ValueVersion = SourceVersion;
    }

    // editor-specific function
    internal void SetUpdated()
    {
        if (_lastUpdateTick >= _globalTickCount && _lastUpdateTick < _globalTickCount + GlobalTickDiffPerFrame - 1)
        {
            _lastUpdateTick++;
        }
        else
        {
            _lastUpdateTick = _globalTickCount;
        }
    }

    public int ValueVersion;
    public int SourceVersion = 1; // initially dirty

    // editor-specific value
    public int FramesSinceLastUpdate => (_globalTickCount - 1 - _lastUpdateTick) / GlobalTickDiffPerFrame;

    public DirtyFlagTrigger Trigger
    {
        get => _trigger;
        set
        {
            _trigger = value;
            TriggerIsEnabled = value != DirtyFlagTrigger.None;
            TriggerIsAnimated = value == DirtyFlagTrigger.Animated;
        }
    }

    private DirtyFlagTrigger _trigger;
    internal bool TriggerIsEnabled;
    internal bool TriggerIsAnimated;

    // editor-specific value
    public int NumUpdatesWithinFrame
    {
        get
        {
            var updatesSinceLastFrame = _lastUpdateTick - _globalTickCount;
            var shift = (-updatesSinceLastFrame >= GlobalTickDiffPerFrame) ? GlobalTickDiffPerFrame : 0;
            return Math.Max(updatesSinceLastFrame + shift + 1, 0); // shift corrects if update was one frame ago
        }
    }

    internal int InvalidatedWithRefFrame = -1;
    private const int GlobalTickDiffPerFrame = 100; // each frame differs with 100 ticks to last one
    private static int _globalTickCount;
    private int _lastUpdateTick;
}