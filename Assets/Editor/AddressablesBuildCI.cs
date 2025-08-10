#if UNITY_EDITOR
using System;
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Build;     // AddressablesPlayerBuildResult
using UnityEditor.AddressableAssets.Settings;
using UnityEngine;

public static class AddressablesBuildCI
{
    [MenuItem("Tools/Content/Build Addressables + packs.json")]
    public static void BuildWithPacks()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("Addressables settings not found."); return; }

        // Keep iteration smooth: ensure rooms are indexed before we build.
        try { RoomsIndexerSimple.IndexRooms(); } catch (Exception e) { Debug.LogWarning($"[Build] IndexRooms skipped: {e.Message}"); }

        EnforceCatalogDefaults(settings);

        // Build using the ACTIVE PROFILE as-is (no path changes here).
        AddressablesPlayerBuildResult result;
        AddressableAssetSettings.BuildPlayerContent(out result);
        if (!string.IsNullOrEmpty(result.Error)) { Debug.LogError(result.Error); return; }

        var outDir = GetOutputFolderFromProfile(settings);
        var catalogBin = FindLatestCatalogBin(outDir);
        if (string.IsNullOrEmpty(catalogBin))
        {
            Debug.LogError("No catalog_*.bin found after build at: " + outDir + "\nCheck your active profile's Remote Build Path.");
            return;
        }

        WritePacksJson(outDir, Path.GetFileName(catalogBin));
        Debug.Log($"[Addressables] Build complete. Output → {outDir}\n - {Path.GetFileName(catalogBin)}\n - packs.json");
    }

    private static void EnforceCatalogDefaults(AddressableAssetSettings settings)
    {
        bool dirty = false;
        if (settings.EnableJsonCatalog) { settings.EnableJsonCatalog = false; dirty = true; } // binary catalog
        if (!settings.BuildRemoteCatalog) { settings.BuildRemoteCatalog = true; dirty = true; }
        if (dirty) { EditorUtility.SetDirty(settings); AssetDatabase.SaveAssets(); }
    }

    private static string GetOutputFolderFromProfile(AddressableAssetSettings settings)
    {
        var ps = settings.profileSettings; var pid = settings.activeProfileId;
        if (ps == null || string.IsNullOrEmpty(pid))
            return null;

        var varValue = ps.GetValueByName(pid, AddressableAssetSettings.kRemoteBuildPath);
        if (string.IsNullOrEmpty(varValue))
            return null;

        var eval = ps.EvaluateString(pid, varValue);
        eval = eval?.Replace("[BuildTarget]", EditorUserBuildSettings.activeBuildTarget.ToString());
        return string.IsNullOrEmpty(eval) ? null : Path.GetFullPath(eval);
    }

    private static string FindLatestCatalogBin(string outDir)
    {
        try
        {
            var f = new DirectoryInfo(outDir).EnumerateFiles("catalog_*.bin")
                    .OrderByDescending(x => x.LastWriteTimeUtc).FirstOrDefault();
            return f?.FullName;
        }
        catch { return null; }
    }

    [Serializable]
    private class PackInfo
    {
        public string id;
        public string title;
        public string releaseUtc;
        public string catalogFile;
    }

    [Serializable]
    private class PacksRoot
    {
        public int version;
        public PackInfo[] packs;
    }

    private static void WritePacksJson(string outDir, string catalogFileName)
    {
        var root = new PacksRoot
        {
            version = 1,
            packs = new[]
            {
                new PackInfo
                {
                    id = "base",
                    title = "Base Content",
                    releaseUtc = DateTime.UtcNow.ToString("O"),
                    catalogFile = catalogFileName
                }
            }
        };

        var json = JsonUtility.ToJson(root, true); // pretty-print
        File.WriteAllText(Path.Combine(outDir, "packs.json"), json);
        AssetDatabase.Refresh();
    }
}
#endif