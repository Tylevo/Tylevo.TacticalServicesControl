# Service icons

These six original symbols use transparent backgrounds and a single foreground
color. The service list, purchase review, and phone share the same icon files.
They are TSC artwork, not assets extracted from EFT.

`Generate-NativeIcons.ps1` contains the 96-unit vector geometry. It exports
512 x 512 RGBA PNGs in ivory (`neutral_512`), amber (`amber_512`), and white
(`mask_512`), plus SVGs and a preview at 52, 72, and 128 pixels. The six SVGs
in `source/` are the ivory reference exports. Edit the geometry in the script
to regenerate all three variants together.

Run in Windows PowerShell with an output directory outside the repository:

```powershell
.\tools\artwork\service-icons\Generate-NativeIcons.ps1 -OutputDirectory C:\Temp\TscServiceIcons
```

The generator uses Windows System.Drawing and needs no additional software.
Review `native-icons-preview.png`, then copy the six generated PNGs from each
variant into the matching directory under
`project/SamSWAT.FireSupport/CopyToOutput/assets/content/ui/phone/icons/`.
Keep other icons in those directories. Generation alone does not install
anything into SPT.

| Filename | Service |
| --- | --- |
| `a10_strafe` | A-10 Strafe |
| `double_pass` | A-10 Double Pass |
| `extraction` | UH-60 Extraction |
| `priority_exfil` | UH-60 Cargo Transfer |
| `uav_recon` | UAV Recon |
| `focused_sweep` | UAV Focused Sweep |
