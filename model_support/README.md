# Independent model support 1.3.7 / R460

This source requires the game's generated IL2CPP assemblies, BepInEx, Harmony and Unity/MagicaCloth interop. It has no Koikatsu/RX22/HeroesVowCardPreview reference. Do not add those assemblies to the build.

The tested FramePacing.dll is embedded as Laevat.ModelSupport460.dll in the R460 model runtime. ModelSupport460.Ensure runs once on the first PreviewDriver.Update after plugin discovery. An existing initialized 1.3.7+ support plugin is reused; otherwise the embedded plugin starts itself. This keeps the signed R458/R459 updater's runtime-only allowlist intact. Old experimental standalone support is rejected rather than double-hooked.

The full installer and cumulative update carry the same runtime. No game/model source assets, private signing material, user saves or diagnostics belong in this repository. Core gameplay and Koikatsu remain separately owned.

Hook tests use mock Unity objects and real Harmony. They are not evidence of a particular in-game frame rate. Original native game members must be checked against the target game build when rebuilding.
