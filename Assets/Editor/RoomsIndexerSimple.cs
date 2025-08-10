#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;
using UnityEngine;

/// <summary>
/// Marks all prefabs under the rooms folder as Addressables with addresses "rooms/<file-name>".
/// Minimal, robust, and package-version friendly.
/// </summary>
public static class RoomsIndexerSimple
{
    // Adjust if your folder structure changes
    private const string RoomsFolder = "Assets/Content/Addressables/base/rooms";
    private const string GroupName   = "rooms_base"; // the Addressables group to hold room prefabs

    [MenuItem("Tools/Content/Index Rooms (simple)")]
    public static void IndexRooms()
    {
        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (settings == null) { Debug.LogError("Addressables settings not found."); return; }

        if (!AssetDatabase.IsValidFolder(RoomsFolder))
        {
            Debug.LogWarning($"Rooms folder not found: {RoomsFolder}");
            return;
        }

        var group = settings.FindGroup(GroupName) ?? settings.CreateGroup(
            GroupName, false, false, false, null);

        // Ensure bundled schema & content update schema exist and are configured
        var bundled = group.GetSchema<BundledAssetGroupSchema>();
        if (bundled == null)
            bundled = group.AddSchema<BundledAssetGroupSchema>();
        var update = group.GetSchema<ContentUpdateGroupSchema>();
        if (update == null)
            update = group.AddSchema<ContentUpdateGroupSchema>();

        // Minimal, version-agnostic settings: include in build, use Remote profile vars for paths
        bundled.IncludeInBuild = true;
        // Addressables 2.5+: BuildPath/LoadPath are read-only references; set their variables only.
        if (bundled.BuildPath != null)
            bundled.BuildPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteBuildPath);
        else
            Debug.LogWarning($"[RoomsIndexer] '{GroupName}' BundledAssetGroupSchema.BuildPath is null; leaving default.");

        if (bundled.LoadPath != null)
            bundled.LoadPath.SetVariableByName(settings, AddressableAssetSettings.kRemoteLoadPath);
        else
            Debug.LogWarning($"[RoomsIndexer] '{GroupName}' BundledAssetGroupSchema.LoadPath is null; leaving default.");

        // Optional (safe) defaults that exist across many Addressables versions
        try { bundled.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash; } catch { }
        try { bundled.Compression  = BundledAssetGroupSchema.BundleCompressionMode.LZ4; } catch { }

        int updated = 0, moved = 0;
        var guids = AssetDatabase.FindAssets("t:Prefab", new[] { RoomsFolder });
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (!path.EndsWith(".prefab", System.StringComparison.OrdinalIgnoreCase))
                continue;

            // Ensure entry exists and is under the correct group
            var entry = settings.FindAssetEntry(guid);
            if (entry == null)
            {
                entry = settings.CreateOrMoveEntry(guid, group);
                moved++; // treated as moved/created into group
            }
            else if (entry.parentGroup != group)
            {
                settings.MoveEntry(entry, group);
                moved++;
            }

            var expectedAddress = "rooms/" + Path.GetFileNameWithoutExtension(path);
            if (entry.address != expectedAddress)
            {
                entry.SetAddress(expectedAddress);
                if (entry.ReadOnly) entry.ReadOnly = false;
                updated++;
            }
        }

        // Persist changes
        EditorUtility.SetDirty(settings);
        AssetDatabase.SaveAssets();
        AssetDatabase.Refresh();

        Debug.Log($"[RoomsIndexer] Group='{GroupName}' Prefabs processed={guids.Length} moved={moved} addresses set/updated={updated}. IncludeInBuild={bundled.IncludeInBuild}");
    }
}
#endif