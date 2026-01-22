#nullable enable
using T3.Core.Resource.Assets;

namespace T3.Editor.Gui.Windows.AssetLib;

/// <summary>
/// Helper to count and display how often assets are used.
/// </summary>
public static class AssetUseCounter {

    public static void IncrementUseCount(AssetType assetType)
    {
        if (Counts.Length != AssetType.AvailableTypes.Count)
        {
            Counts = new int[AssetType.AvailableTypes.Count];
        }

        Counts[assetType.Index]++;
    }

    internal static int GetUseCount(AssetType assetType)
    {
        if (assetType.Index >= Counts.Length)
            return 0;
        
        return Counts[assetType.Index];
    } 
    
    internal static void ClearMatchingFileCounts()
    {
        Array.Clear(Counts, 0, Counts.Length);
    }
    
    public static int[] Counts;
}