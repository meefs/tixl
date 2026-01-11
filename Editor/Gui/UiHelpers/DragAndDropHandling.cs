#nullable enable
using System.Diagnostics.CodeAnalysis;
using System.Runtime.InteropServices;
using ImGuiNET;
using T3.Core.DataTypes.Vector;

namespace T3.Editor.Gui.UiHelpers;

internal static class DragAndDropHandling
{
    /// <summary>
    /// This should be called once per frame 
    /// </summary>
    internal static void Update()
    {
        if (IsDragging && _stopRequested)
        {
            FreeData();
            _stopRequested = false;
            _activeDraggingId =DragTypes.None;
        }
    }
    
    /// <summary>
    /// This should be called right after an ImGui item that is a drag source (e.g. a button).
    /// </summary>
    internal static void HandleDragSourceForLastItem(DragTypes dragType, string data, string dragLabel)
    {
        if (ImGui.IsItemActive())
        {
            if (IsDragging || !ImGui.BeginDragDropSource())
                return;
            
            if(HasData)
                FreeData();
            
            _dropData = Marshal.StringToHGlobalUni(data);
            _activeDraggingId = dragType;

            ImGui.SetDragDropPayload(dragType.ToString(), _dropData, (uint)((data.Length +1) * sizeof(char)));

            ImGui.Button(dragLabel);
            ImGui.EndDragDropSource();
        }
        else if (ImGui.IsItemDeactivated())
        {
            StopDragging();
        }
    }

    /// <summary>
    /// Checks if data is valid for the passed DragId
    /// </summary>
    /// <returns>
    /// True if dropped
    /// </returns>
    internal static bool TryHandleItemDrop(DragTypes dragType, [NotNullWhen(true)] out string? data)
    {
        data = string.Empty;
        
        if (!IsDragging)
            return false;
        
        var isHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenBlockedByActiveItem);
        var fade = isHovered ? 1 : 0.5f;
        
        ImGui.GetForegroundDrawList()
             .AddRect(ImGui.GetItemRectMin(), ImGui.GetItemRectMax(), 
                      Color.Orange.Fade(fade));

        if (!isHovered)
            return false;
        
        if (!ImGui.BeginDragDropTarget())
            return false;
        
        var success = false;
        var payload = ImGui.AcceptDragDropPayload(dragType.ToString());
        if (ImGui.IsMouseReleased(0))
        {
            if (HasData)
            {
                try
                {
                    data = Marshal.PtrToStringAuto(payload.Data);
                    success = data != null;
                }
                catch (Exception e)
                {
                    Log.Warning(" Failed to get drop data " + e.Message);
                }
            }
            else
            {
                Log.Warning("No data for drop?");
            }
            _activeDraggingId = DragTypes.None;
        }

        ImGui.EndDragDropTarget();

        return success;
    }

    internal enum DragInteractionResult
    {
        None,
        Invalid,
        Hovering,
        Dropped,
    }
    
    /// <summary>
    /// To prevent inconsistencies related to the order of window processing,
    /// we have to defer the end until beginning of 
    /// </summary>
    private static void StopDragging()
    {
        _stopRequested = true;
    }

    private static void FreeData()
    {
        if (!HasData)
            return;
        
        Marshal.FreeHGlobal(_dropData);
        _dropData = IntPtr.Zero; // Prevent double free
    }


    private static DragTypes _activeDraggingId = DragTypes.None; 
    internal static bool IsDragging => _activeDraggingId != DragTypes.None;

    internal static bool IsDraggingWith(DragTypes dragType)
    {
        return _activeDraggingId == dragType;
    }
    
    private static bool HasData => _dropData != IntPtr.Zero;

    private static IntPtr _dropData = new(0);
    private static bool _stopRequested;

    // TODO: Should be an enumeration
    internal enum DragTypes
    {
        None,
        Symbol,
        FileAsset,
        ExternalFile,
    }
    // internal const string SymbolDraggingId = "symbol";
    // internal const string AssetDraggingId = "fileAsset";
}