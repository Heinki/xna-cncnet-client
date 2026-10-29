These transparent white silhouettes are derived from original game sprites and voxel geometry, not generated artwork.

- Titan: Tiberian Sun `conquer.mix/mmch.shp`, body frame 30 plus turret frame 128, on their native shared canvas. Shadow frames are omitted.
- Tick Tank: Tiberian Sun `local.mix/ttnk.vxl`, `ttnktur.vxl`, `ttnkbarl.vxl`.
- Grizzly: Red Alert 2 `local.mix/mtnk.vxl`, `mtnktur.vxl`, `mtnkbarl.vxl`.
- Rhino: Red Alert 2 `local.mix/htnk.vxl`, `htnktur.vxl`, `htnkbarl.vxl`.
- Lasher: Yuri's Revenge `localmd.mix/ltnk.vxl`, `ltnktur.vxl`, plus the base `local.mix/ltnkbarl.vxl`.

Voxel silhouettes use an orthographic isometric projection of occupied native voxels with each section's stored bounds. Palette index zero is transparent in SHP frames. Images are cropped and bounded to 128 pixels; marker ownership tint is applied by the client. Game packs can override these defaults through `[ConquestArmyIcons]` and `[ConquestArmyStyles]` in GameOptions.ini.
