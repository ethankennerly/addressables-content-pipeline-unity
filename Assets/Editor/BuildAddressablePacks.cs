#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using UnityEditor;
using UnityEngine;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

[Serializable] class packdto { public string id; public string title; public string releaseUtc; public string catalogFile; }
[Serializable] class buildcfgdto { public int version = 1; public packdto[] packs; }
[Serializable] class manifestdto { public int version = 1; public packdto[] packs; }

public static class BuildAddressablePacks
{
    private const string ConfigPath = "Assets/Content/packs.build.json";
    private const string BuildLayoutPath = "Library/com.unity.addressables/buildlayout.json";
    private const string AaRoot = "Library/com.unity.addressables/aa";

    [MenuItem("Build/Addressables/Build All Packs (from packs.build.json)")]
    public static void BuildAll()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (!settings)
        {
            Debug.LogError("Addressables not set up (Window > Asset Management > Addressables > Groups).");
            return;
        }

        settings.UniqueBundleIds = true;
        settings.EnableJsonCatalog = true;
        EnsureSettingsProfileRefs(settings);
        
        if (!File.Exists(ConfigPath))
        {
            Debug.LogError("Missing config file at " + Path.GetFullPath(ConfigPath));
            return;
        }
        var cfg = JsonUtility.FromJson<buildcfgdto>(File.ReadAllText(ConfigPath));
        if (cfg?.packs == null || cfg.packs.Length == 0)
        {
            Debug.LogError("No packs defined in " + ConfigPath);
            return;
        }

        string platform = EditorUserBuildSettings.activeBuildTarget.ToString();
        string externalOutDir = Path.GetFullPath(Path.Combine("..", "ServerData", platform));
        Directory.CreateDirectory(externalOutDir);

        foreach (var pack in cfg.packs)
        {
            ToggleGroupsForPack(settings, pack.id);
            EnsureSchemaIdsForAllGroups(settings);   // ← silence “empty id” warnings globally

            AddressableAssetSettings.CleanPlayerContent();
            AddressablesPlayerBuildResult result;
            AddressableAssetSettings.BuildPlayerContent(out result);
            if (!string.IsNullOrEmpty(result.Error))
            {
                Debug.LogError(result.Error);
                return;
            }

            // Prefer layout parse; fallback to AA folder scan.
            if (!TryGetCatalogFromBuildLayout(out var srcCatalogPath, out var localRoot))
            {
                if (!TryFindCatalogByScan(out srcCatalogPath, out localRoot))
                {
                    Debug.LogError("Could not locate catalog after build (checked buildlayout.json and AA folder).");
                    return;
                }
            }

            int copiedCatalog = MirrorCatalog(srcCatalogPath, externalOutDir, pack.catalogFile);
            int copiedBundles = MirrorBundles(localRoot, platform, externalOutDir);

            Debug.Log($"Built pack '{pack.id}': catalogs={copiedCatalog}, bundles={copiedBundles}");
        }

        var manifest = new manifestdto { version = 1, packs = cfg.packs };
        File.WriteAllText(Path.Combine(externalOutDir, "packs.json"), JsonUtility.ToJson(manifest, true), System.Text.Encoding.UTF8);

        Debug.Log($"Addressables artifacts ready: {externalOutDir}");
        AssetDatabase.Refresh();
    }
    
    private static void EnsureSettingsProfileRefs(AddressableAssetSettings s)
    {
        // Some projects leave these unset; Addressables still tries to Evaluate() them during build.
        // Assign Local.* to avoid "empty id" warnings without changing behavior.
        try
        {
            if (s.RemoteCatalogLoadPath != null && string.IsNullOrEmpty(s.RemoteCatalogLoadPath.Id))
                s.RemoteCatalogLoadPath.SetVariableByName(s, AddressableAssetSettings.kLocalLoadPath);

    #if UNITY_6000_0_OR_NEWER
            // Unity 6+ also has RemoteCatalogBuildPath in some versions
            var prop = typeof(AddressableAssetSettings).GetProperty("RemoteCatalogBuildPath");
            var val = prop?.GetValue(s, null) as ProfileValueReference;
            if (val != null && string.IsNullOrEmpty(val.Id))
                val.SetVariableByName(s, AddressableAssetSettings.kLocalBuildPath);
    #endif
        }
        catch { /* best-effort; safe if fields not present in this Addressables version */ }
    }

    // Enable only Shared + groups that contain the pack id (case-insensitive)
    private static void ToggleGroupsForPack(AddressableAssetSettings s, string packId)
    {
        string idLower = packId.ToLowerInvariant();
        foreach (var g in s.groups)
        {
            if (g == null) continue;
            var schema = g.GetSchema<BundledAssetGroupSchema>();
            if (schema == null) continue;

            string n = g.Name.ToLowerInvariant();
            bool isShared = n.StartsWith("shared");
            bool isPack = n.Contains(idLower);

            schema.IncludeInBuild = isShared || isPack;
            EditorUtility.SetDirty(schema);
        }
        AssetDatabase.SaveAssets();
    }

    // Assign known profile variable IDs on ALL groups to avoid “empty id” warnings anywhere.
    private static void EnsureSchemaIdsForAllGroups(AddressableAssetSettings s)
    {
        foreach (var g in s.groups)
        {
            var schema = g?.GetSchema<BundledAssetGroupSchema>();
            if (schema == null) continue;

            if (string.IsNullOrEmpty(schema.BuildPath?.Id))
                schema.BuildPath.SetVariableByName(s, AddressableAssetSettings.kLocalBuildPath);
            if (string.IsNullOrEmpty(schema.LoadPath?.Id))
                schema.LoadPath.SetVariableByName(s,  AddressableAssetSettings.kLocalLoadPath);

            EditorUtility.SetDirty(schema);
        }
        AssetDatabase.SaveAssets();
    }

    // Parse buildlayout.json to get the last catalog path and LocalCatalogBuildPath
    private static bool TryGetCatalogFromBuildLayout(out string catalogPath, out string localRoot)
    {
        catalogPath = null;
        localRoot = null;

        string layoutAbs = Path.GetFullPath(BuildLayoutPath);
        if (!File.Exists(layoutAbs)) return false;

        string json = File.ReadAllText(layoutAbs);

        // Prefer JSON if present; else BIN from CatalogLoadPaths
        var rxPaths = new Regex(@"""CatalogLoadPaths""\s*:\s*\[(.*?)\]", RegexOptions.Singleline | RegexOptions.IgnoreCase);
        var m = rxPaths.Match(json);
        if (m.Success)
        {
            var inner = m.Groups[1].Value;
            var match = Regex.Matches(inner, @"""([^""]*catalog\.(json|bin))""", RegexOptions.IgnoreCase)
                             .Cast<Match>().Select(mm => mm.Groups[1].Value).LastOrDefault();
            if (!string.IsNullOrEmpty(match))
            {
                var abs = Path.IsPathRooted(match) ? match : Path.GetFullPath(match);
                if (File.Exists(abs)) catalogPath = abs;
            }
        }

        var rxLocal = new Regex(@"""LocalCatalogBuildPath""\s*:\s*""([^""]+)""", RegexOptions.IgnoreCase);
        var m2 = rxLocal.Match(json);
        if (m2.Success)
        {
            var root = m2.Groups[1].Value;
            localRoot = Path.IsPathRooted(root) ? root : Path.GetFullPath(root);
        }

        if (catalogPath == null && !string.IsNullOrEmpty(localRoot) && Directory.Exists(localRoot))
        {
            var cats = Directory.GetFiles(localRoot, "catalog.*", SearchOption.AllDirectories)
                                .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                            p.EndsWith(".bin",  StringComparison.OrdinalIgnoreCase))
                                .OrderByDescending(File.GetLastWriteTimeUtc)
                                .ToArray();
            if (cats.Length > 0) catalogPath = cats[0];
        }

        return catalogPath != null && !string.IsNullOrEmpty(localRoot);
    }

    // Absolute fallback if layout parsing fails
    private static bool TryFindCatalogByScan(out string catalogPath, out string localRoot)
    {
        catalogPath = null;
        localRoot = null;

        string aaAbs = Path.GetFullPath(AaRoot);
        if (!Directory.Exists(aaAbs)) return false;

        var cats = Directory.GetFiles(aaAbs, "catalog.*", SearchOption.AllDirectories)
                            .Where(p => p.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                                        p.EndsWith(".bin",  StringComparison.OrdinalIgnoreCase))
                            .OrderByDescending(File.GetLastWriteTimeUtc)
                            .ToArray();
        if (cats.Length == 0) return false;

        catalogPath = cats[0];

        // localRoot is the nearest ancestor AA folder (usually .../aa/<Platform> or /aa/<Platform>/<Platform>)
        var dir = Path.GetDirectoryName(catalogPath)!;
        // Walk up to the first child of /aa
        while (dir != null && Path.GetFileName(Path.GetDirectoryName(dir) ?? "") != "aa")
            dir = Path.GetDirectoryName(dir);
        localRoot = dir ?? aaAbs;
        return true;
    }

    // Copy the catalog to external with desired name (.json preferred; if .bin, keep .bin)
    private static int MirrorCatalog(string srcCatalogPath, string destDir, string desiredName)
    {
        string finalName = srcCatalogPath.EndsWith(".bin", StringComparison.OrdinalIgnoreCase)
            ? Path.ChangeExtension(desiredName, ".bin")
            : Path.ChangeExtension(desiredName, ".json");

        string destCatalog = Path.Combine(destDir, finalName);
        File.Copy(srcCatalogPath, destCatalog, true);

        string srcHash = Path.ChangeExtension(srcCatalogPath, ".hash");
        if (File.Exists(srcHash))
        {
            string destHash = Path.ChangeExtension(destCatalog, ".hash");
            File.Copy(srcHash, destHash, true);
        }
        return 1;
    }

    // Copy *.bundle (+ .hash) from localRoot/<Platform>(/<Platform>)/** into destDir, flattening duplicate /<Platform>
    private static int MirrorBundles(string localRoot, string platform, string destDir)
    {
        if (string.IsNullOrEmpty(localRoot) || !Directory.Exists(localRoot)) return 0;

        string deepPlatform    = Path.Combine(localRoot, platform, platform);
        string shallowPlatform = Path.Combine(localRoot, platform);
        string bundlesRoot = Directory.Exists(deepPlatform) ? deepPlatform :
                             Directory.Exists(shallowPlatform) ? shallowPlatform :
                             localRoot;

        var bundleFiles = Directory.GetFiles(bundlesRoot, "*.bundle", SearchOption.AllDirectories);
        int copied = 0;
        foreach (var src in bundleFiles)
        {
            string rel = Path.GetRelativePath(bundlesRoot, src).Replace('\\', '/'); // no extra /Platform
            string destPath = Path.Combine(destDir, rel);
            Directory.CreateDirectory(Path.GetDirectoryName(destPath)!);
            File.Copy(src, destPath, true);
            copied++;

            var srcHash = Path.ChangeExtension(src, ".hash");
            if (File.Exists(srcHash))
            {
                var destHash = Path.ChangeExtension(destPath, ".hash");
                File.Copy(srcHash, destHash, true);
            }
        }
        return copied;
    }
}
#endif