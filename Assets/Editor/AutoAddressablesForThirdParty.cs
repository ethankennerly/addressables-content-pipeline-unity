#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEditor.AddressableAssets;
using UnityEditor.AddressableAssets.Settings;
using UnityEditor.AddressableAssets.Settings.GroupSchemas;

/// <summary>
/// Auto-assign Addressables entries for FBX dropped under:
/// Assets/Content/ThirdParty/Kenney/FurniturePack/Models/<pack>/<category>/*.fbx
/// No prefabs required. Uses the FBX main model prefab.
/// </summary>
public class AutoAddressablesForThirdParty : AssetPostprocessor
{
    // Root we watch
    const string Root = "Assets/Content/ThirdParty/Kenney/FurnitureKit/Models/";

    // Called on import/move
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        foreach (var path in imported)
        {
            TryAssignAddressable(path);
        }
        foreach (var path in moved)
        {
            TryAssignAddressable(path);
        }
    }

    [MenuItem("Tools/Content/Reindex ThirdParty/Kenney FurniturePack")]
    static void ReindexAll()
    {
        var guids = AssetDatabase.FindAssets("t:Model", new[] { Root.TrimEnd('/') });
        int count = 0;
        foreach (var guid in guids)
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (TryAssignAddressable(path)) count++;
        }
        Debug.Log($"[AutoAddressables] Reindexed {count} models under {Root}");
    }

    static bool TryAssignAddressable(string assetPath)
    {
        if (!assetPath.EndsWith(".fbx", System.StringComparison.OrdinalIgnoreCase)) return false;

        var norm = assetPath.Replace("\\", "/");
        if (!norm.StartsWith(Root)) return false;

        // Expect: .../Models/<pack>/<category>/<file>.fbx
        var rel = norm.Substring(Root.Length); // <pack>/<category>/<file>.fbx
        var parts = rel.Split('/');
        if (parts.Length < 2)
        {
            Debug.LogWarning($"[AutoAddressables] Could not infer pack/category for {assetPath}");
            return false;
        }

        string pack = parts[0];               // e.g., "base" or "2025-08-09"
        string category = parts[1].ToLower(); // e.g., "rooms", "sofas", "lamps", ...

        var settings = AddressableAssetSettingsDefaultObject.Settings;
        if (!settings)
        {
            Debug.LogError("[AutoAddressables] Addressables settings not found.");
            return false;
        }

        // Group selection: Rooms_<pack> vs Furniture_<pack>
        bool isRoom = category == "rooms" || category == "room";
        string groupName = isRoom ? $"Rooms_{pack}" : $"Furniture_{pack}";
        var group = EnsureBundledGroup(settings, groupName);

        // FBX main asset is the model prefab Unity generates
        var main = AssetDatabase.LoadMainAssetAtPath(assetPath) as GameObject;
        if (!main)
        {
            // Not a model (might be dependency file), skip
            return false;
        }

        // Address + label
        string fileNoExt = Path.GetFileNameWithoutExtension(assetPath);
        string address, label;
        if (isRoom)
        {
            address = $"rooms/{fileNoExt}";
            label = "room";
        }
        else
        {
            // map category folder to label token (e.g., sofas -> furniture:sofa)
            string cat = Singularize(category); // "sofas" -> "sofa"
            address = $"furniture/{cat}/{fileNoExt}";
            label = $"furniture:{cat}";
        }

        // Create/move entry by GUID (FBX main asset)
        string guid = AssetDatabase.AssetPathToGUID(assetPath);
        var entry = settings.CreateOrMoveEntry(guid, group);
        entry.address = address;
        entry.SetLabel(label, true, true);

        // Optional: pack label if you want to filter by pack at runtime/editor
        entry.SetLabel($"pack:{pack}", true, true);

        // Keep assets saved
        AssetDatabase.SaveAssets();

        // Log once per file
        // Debug.Log($"[AutoAddressables] {Path.GetFileName(assetPath)} → Group '{groupName}', address '{address}', label '{label}'");
        return true;
    }

    static AddressableAssetGroup EnsureBundledGroup(AddressableAssetSettings s, string name)
    {
        var g = s.FindGroup(name) ?? s.CreateGroup(name, false, false, true, null);
        var schema = g.GetSchema<BundledAssetGroupSchema>() ?? g.AddSchema<BundledAssetGroupSchema>();
        schema.IncludeInBuild = true; // Build script will flip which groups are included per catalog
        schema.BundleMode = BundledAssetGroupSchema.BundlePackingMode.PackTogether;
        schema.BundleNaming = BundledAssetGroupSchema.BundleNamingStyle.AppendHash;
        return g;
    }

    static string Singularize(string plural)
    {
        // Very small helper for common English category plurals
        plural = plural.ToLowerInvariant();
        if (plural.EndsWith("s")) return plural.TrimEnd('s');
        return plural;
    }
}
#endif