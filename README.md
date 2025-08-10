# addressables-content-pipeline-unity

Builds and publishes **Unity Addressables** asset bundles and a catalog.  
This is the content side of a two‑repo architecture. For project purpose and collaboration, see the [Project README](https://github.com/ethankennerly/addressables-runtime-client-unity/tree/main/Documentation).

## Purpose
This repository contains Unity editor configuration and minimal tooling to package game content into Addressables AssetBundles and produce a catalog for the runtime client to load remotely. It supports both local file‑based testing and production deployment.

## Requirements
- Unity 2022.3 LTS or newer  
- Addressables package (installed via Package Manager)

## Usage

1. **Open in Unity**  
   Open this repository in the Unity Editor.

2. **Prepare Addressables Groups**  
   - Place/add assets under `Assets/Content/Addressables/` (e.g., `base/rooms`, `base/furniture`).  
   - In **Window → Asset Management → Addressables → Groups**, confirm assets are in the expected groups and labels are set (e.g., `furniture:chair`).

3. **Build Content**  
   - In the Addressables Groups window: **Build → New Build → Default Build Script**.  
   - Use profiles so the **Remote Build/Load Path** points to your output folder. The standard local path is:
     `../ServerData/[BuildTarget]` (resolved to an absolute path on disk).

4. **Publish**  
   Copy or upload the generated `.bundle`, `.bin`, `.hash` files (and any manifest you use) from `../ServerData/<Platform>` to your host/CDN.

## Output (example)
```
../ServerData/StandaloneOSX
• catalog_*.bin
• catalog_*.hash
• rooms_base__xxxxxxxx.bundle
• furniture_yyyyyyyy.bundle
```

## Related Repository
Runtime client that consumes this content:  
[https://github.com/ethankennerly/addressables-runtime-client-unity](https://github.com/ethankennerly/addressables-runtime-client-unity)

## License
MIT
