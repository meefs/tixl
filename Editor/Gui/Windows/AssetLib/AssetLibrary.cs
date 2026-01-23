#nullable enable

using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using ImGuiNET;
using T3.Core.Operator;
using T3.Core.Operator.Slots;
using T3.Core.Resource;
using T3.Core.Resource.Assets;
using T3.Core.UserData;
using T3.Core.Utils;
using T3.Editor.Gui.InputUi.SimpleInputUis;
using T3.Editor.Gui.UiHelpers;
using T3.Editor.UiModel;
using T3.Editor.UiModel.Helpers;
using T3.Editor.UiModel.ProjectHandling;
using T3.Editor.UiModel.Selection;

namespace T3.Editor.Gui.Windows.AssetLib;

/// <summary>
/// Shows a tree of all defined symbols sorted by namespace 
/// </summary>
internal sealed partial class AssetLibrary : Window
{
    internal AssetLibrary()
    {
        _state.Filter.SearchString = "";
        Config.Title = "Assets";
    }

    internal override List<Window> GetInstances()
    {
        return [];
    }

    protected override void DrawContent()
    {
        // Init current frame
        UpdateAssetsIfRequired();
        if (_state.Composition == null)
            return;

        if (!NodeSelection.TryGetSelectedInstanceOrInput(out var selectedInstance, out _, out var selectionChanged))
        {
            selectedInstance = _state.Composition;
        }

        UpdateActiveSelection(selectedInstance);

        // Draw
        DrawLibContent();
    }

    // TODO: refactor this to asset registry
    private void UpdateAssetsIfRequired()
    {
        _state.Composition = ProjectView.Focused?.CompositionInstance;
        if (_state.Composition == null)
            return;

        if (_state.LastFileWatcherState == ResourceFileWatcher.FileStateChangeCounter
            && !Core.Utils.Utilities.HasObjectChanged(_state.Composition, ref _lastCompositionObjId)
            && !_state.FilteringNeedsUpdate)
            return;

        _state.TreeHandler.Reset();
        _state.LastFileWatcherState = ResourceFileWatcher.FileStateChangeCounter;

        _state.AllAssets.Clear();
        AssetUseCounter.ClearMatchingFileCounts();
        
        var filePaths = ResourceManager.EnumeratePackagesResources([],
                                                           isFolder: false,
                                                           _state.Composition.AvailableResourcePackages,
                                                           ResourceManager.PathMode.PackageUri);

        foreach (var aliasedPath in filePaths)
        {
            // Not sure, if this works...
            var found = false;
            foreach (var filter in FileLocations.IgnoredFiles)
            {
                if(StringUtils.MatchesSearchFilter(aliasedPath,filter, true))
                {
                    found = true;
                    break;
                }
            }

            if (found)
                continue;
            
            
            if (!_state.AssetCache.TryGetValue(aliasedPath, out var asset))
            {
                if (!AssetRegistry.TryResolveUri(aliasedPath, _state.Composition, out var absolutePath, out var package))
                {
                    Log.Warning($"Can't find file {aliasedPath}");
                    continue;
                }

                ParsePath(aliasedPath, out var packageName, out var folders);

                var fileInfo = new FileInfo(absolutePath);
                var fileInfoExtension = fileInfo.Extension.Length < 1 ? string.Empty : fileInfo.Extension[1..];
                var fileExtensionId = FileExtensionRegistry.GetUniqueId(fileInfoExtension);
                if (!AssetType.TryGetFromExtensionId(fileExtensionId, out var assetType))
                {
                    Log.Warning($"Can't find file type for: {fileInfoExtension}");
                }

                asset = new AssetItem
                            {
                                FileAliasPath = aliasedPath,
                                FileInfo = fileInfo,
                                Package = package,
                                PackageName = packageName,
                                FilePathFolders = folders,
                                AbsolutePath = absolutePath, // With forward slashes
                                FileExtensionId = fileExtensionId,
                                AssetType = assetType,
                            };
                _state.AssetCache[aliasedPath] = asset;
            }
            
            if (asset.AssetType != null)
            {
                AssetUseCounter.IncrementUseCount(asset.AssetType);
                AssetHandling.TotalAssetCount++;
            }

            if (_state.CompatibleExtensionIds.Count == 0
                || _state.CompatibleExtensionIds.Contains(asset.FileExtensionId))
            {
                _state.AllAssets.Add(asset);
            }
            
        }

        AssetFolder.PopulateCompleteTree(_state, filterAction: null);
        _state.FilteringNeedsUpdate = false;
    }

    private static void ParsePath(string uri, out string package, out List<string> folders)
    {
        // TODO: this should replaced with assetAddress later
        var colon = uri.IndexOf(':');
        if (colon <= 1)
        {
            folders = [];
            package = string.Empty;
            return;
        }

        package = uri[..colon];
        var path = uri[(colon + 1)..];
        
        var parts = path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        
        folders = [package];
        for (var index = 0; index < parts.Length -1; index++)
        {
            folders.Add(parts[index]);
        }
    }

    private static void UpdateActiveSelection(Instance selectedInstance)
    {
        _state.HasActiveInstanceChanged = selectedInstance != _state.ActiveInstance;
        if (!_state.HasActiveInstanceChanged)
            return;

        _state.TimeActiveInstanceChanged = ImGui.GetTime();

        _state.ActiveInstance = selectedInstance;
        _state.ActivePathInput = null;
        _state.ActiveAbsolutePath = null;
        _state.CompatibleExtensionIds.Clear();

        // Check if active instance has asset reference...
        var instance = _state.ActiveInstance;
        
        if (SymbolAnalysis.TryGetFileInputFromInstance(instance, out _state.ActivePathInput, out var stringInputUi))
        {
            var filePath = _state.ActivePathInput.GetCurrentValue();
            AssetRegistry.TryResolveUri(filePath, instance, out _state.ActiveAbsolutePath, out _);

            if (UserSettings.Config.SyncWithOperatorSelection)
            {
                FileExtensionRegistry.IdsFromFileFilter(stringInputUi.FileFilter, ref _state.CompatibleExtensionIds);
                _state.ActiveTypeFilters.Clear();
                foreach (var assetType in AssetType.AvailableTypes)
                {
                    foreach (var extId in _state.CompatibleExtensionIds)
                    {
                        if (!assetType.ExtensionIds.Contains(extId)) 
                            continue;
                        
                        _state.ActiveTypeFilters.Add(assetType);
                        break;
                    }
                }
                
            }
        }
        else
        {
            _state.ActiveAbsolutePath = null;
        }
    }

    internal static bool GetAssetFromAliasPath(string aliasPath, [NotNullWhen(true)] out AssetItem? asset)
    {
        return _state.AssetCache.TryGetValue(aliasPath, out asset);
    }

    private int? _lastCompositionObjId = 0;

    private static readonly AssetLibState _state = new();
}