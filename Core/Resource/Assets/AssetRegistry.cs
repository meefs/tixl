#nullable enable
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.IO;
using System.Linq;
using T3.Core.Logging;
using T3.Core.Model;
using T3.Core.Operator;
using T3.Core.Utils;

namespace T3.Core.Resource.Assets;

public static class AssetRegistry
{
    public static bool TryGetAsset(string address, [NotNullWhen(true)] out Asset? asset)
    {
        return _assetsByAddress.TryGetValue(address, out asset);
    }

    public static bool TryResolveUri(string uri,
                                     IResourceConsumer consumer,
                                     out string absolutePath,
                                     [NotNullWhen(true)] out IResourcePackage? resourceContainer,
                                     bool isFolder = false)
    {
        resourceContainer = null;
        absolutePath = string.Empty;
        
        if (string.IsNullOrWhiteSpace(uri))
        {
            return false;
        }
        
        if (!isFolder)
        {
            if (TryGetAsset(uri, out var asset))
            {
                if(asset.FileInfo != null) 
                {
                    Log.Debug($"Found Asset: {asset}");
                    absolutePath = asset.FileInfo.FullName;
                    resourceContainer = ResourceManager.SharedShaderPackages.FirstOrDefault(c => c.Id == asset.PackageId);
                    return resourceContainer!= null;
                }
            }
        }
        
        uri.ToForwardSlashesUnsafe();
        var uriSpan = uri.AsSpan();
        
        // Fallback for internal editor resources  
        if (uriSpan.StartsWith("./"))
        {
            absolutePath = Path.GetFullPath(uri);
            if (consumer is Instance instance)
            {
                Log.Warning($"Can't resolve relative asset '{uri}'", instance);
            }
            else
            {
                Log.Warning($"Can't relative resolve asset '{uri}'");
            }
        
            return false;
        }
        
        var projectSeparator = uri.IndexOf(':');
        
        // Assume windows legacy file path
        if (projectSeparator == 1)
        {
            absolutePath = uri;
            return Exists(absolutePath, isFolder);
        }
        
        if (projectSeparator == -1)
        {
            Log.Warning($"Can't resolve asset '{uri}'");
            return false;
        }
        
        var packageName = uriSpan[..projectSeparator];
        var localPath = uriSpan[(projectSeparator + 1)..];
        
        var packages = consumer?.AvailableResourcePackages;
        if (packages == null)
        {
            // FIXME: this should be properly implemented
            //packages = uri.EndsWith(".hlsl") ? _shaderPackages : _sharedResourcePackages;
            packages = ResourceManager.ShaderPackages;
        
            if (packages.Count == 0)
            {
                Log.Warning($"Can't resolve asset '{uri}' (no packages)");
                return false;
            }
        }
        
        foreach (var package in packages)
        {
            if (package.Name.AsSpan().Equals(packageName, StringComparison.Ordinal))
            {
                resourceContainer = package;
                absolutePath = $"{package.ResourcesFolder}/{localPath}"; // Path.Combine(package.ResourcesFolder, localPath.ToString());
                var exists = Exists(absolutePath, isFolder);
                return exists;
            }
        }
        
        Log.Warning($"Can't resolve asset '{uri}' (no match in {packages.Count} packages)");
        return false;
    }

    private static bool Exists(string absolutePath, bool isFolder) => isFolder
                                                                          ? Directory.Exists(absolutePath)
                                                                          : File.Exists(absolutePath);

    internal static bool TryConvertToRelativePath(string newPath, [NotNullWhen(true)] out string? relativePath)
    {
        newPath.ToForwardSlashesUnsafe();
        foreach (var package in SymbolPackage.AllPackages)
        {
            var folder = package.ResourcesFolder;
            if (newPath.StartsWith(folder))
            {
                relativePath = $"{package.Name}:{newPath[folder.Length..]}";
                relativePath.ToForwardSlashesUnsafe();
                return true;
            }
        }

        relativePath = null;
        return false;
    }

    /// <summary>
    /// This will try to first create a localUrl, then a packageUrl,
    /// and finally fall back to an absolute path.
    ///
    /// This can be useful to test if path would be valid before the
    /// asset is being registered...
    /// </summary>
    public static bool TryConstructAddressFromFilePath(string absolutePath,
                                                       Instance composition,
                                                       [NotNullWhen(true)] out string assetUri)
    {
        assetUri = null;
        if (string.IsNullOrWhiteSpace(absolutePath)) return false;

        var normalizedPath = absolutePath.Replace("\\", "/");

        var localPackage = composition.Symbol.SymbolPackage;

        // Disable localUris for now
        //var localRoot = localPackage.ResourcesFolder.TrimEnd('/') + "/";
        // if (normalizedPath.StartsWith(localRoot, StringComparison.OrdinalIgnoreCase))
        // {
        //     // Dropping the root folder gives us the local relative path
        //     assetUri = normalizedPath[localRoot.Length..];
        //     return true;
        // }

        // 3. Check other packages
        foreach (var p in composition.AvailableResourcePackages)
        {
            if (p == localPackage) continue;

            var packageRoot = p.ResourcesFolder.TrimEnd('/') + "/";
            if (normalizedPath.StartsWith(packageRoot, StringComparison.OrdinalIgnoreCase))
            {
                // Tixl 4.0 format...
                assetUri = $"/{p.Name}/{normalizedPath[packageRoot.Length..]}";
                return true;
            }
        }

        // 4. Fallback to Absolute
        assetUri = normalizedPath;
        return true;
    }

    #region registration and update
    /// <summary>
    /// Scans a package's resource folder and registers all found files.
    /// </summary>
    internal static void RegisterAssetsFromPackage(SymbolPackage package)
    {
        var root = package.ResourcesFolder;
        if (!Directory.Exists(root)) return;

        // Use standard .NET EnumerateFiles for performance
        var files = Directory.EnumerateFiles(root, "*.*", SearchOption.AllDirectories);
        var packageId = package.Id;
        var packageAlias = package.Name;

        foreach (var filePath in files)
        {
            var relativePath = Path.GetRelativePath(root, filePath).Replace("\\", "/");
            var uri = $"{packageAlias}:{relativePath}"; // Mandatory format

            var fileInfo = new FileInfo(filePath);
            AssetType.TryGetForFilePath(fileInfo.Name, out var assetType);

            var asset = new Asset
                            {
                                Address = uri,
                                PackageId = packageId,
                                FileInfo = fileInfo,
                                AssetType = assetType // To be determined by extension
                            };

            _assetsByAddress[uri] = asset;
        }

        Log.Debug($"{packageAlias}: Registered {_assetsByAddress.Count(a => a.Value.PackageId == packageId)} assets.");
    }

    public static void UnregisterPackage(Guid packageId)
    {
        var urisToRemove = _assetsByAddress.Values
                                           .Where(a => a.PackageId == packageId)
                                           .Select(a => a.Address)
                                           .ToList();

        foreach (var uri in urisToRemove)
        {
            _assetsByAddress.TryRemove(uri, out _);
            _usagesByAddress.TryRemove(uri, out _);
        }
    }
    #endregion

    public const char PathSeparator = '/';
    public const char PackageSeparator = ':';

    private static readonly ConcurrentDictionary<string, Asset> _assetsByAddress = new(StringComparer.OrdinalIgnoreCase);
    private static readonly ConcurrentDictionary<string, List<AssetReference>> _usagesByAddress = new();
}