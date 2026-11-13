param([Parameter(Mandatory=$true)][string]$GamePath,[Parameter(Mandatory=$true)][string]$ReferencePack,[Parameter(Mandatory=$true)][string]$Csc,[string]$Output="FramePacing.dll")
$ErrorActionPreference='Stop'
$g=Join-Path $GamePath 'BepInEx'
$refs=@(Get-ChildItem -LiteralPath $ReferencePack -Filter '*.dll' | ForEach-Object {'-r:'+$_.FullName})
foreach($dll in 'Assembly-CSharp','UnityEngine.CoreModule','MagicaClothV2','UnityEngine.AssetBundleModule','UnityEngine.ImageConversionModule','UnityEngine.ParticleSystemModule','Il2Cppmscorlib','UnityEngine.InputLegacyModule','UnityEngine.IMGUIModule','UnityEngine.TextRenderingModule','UnityEngine.UI','UnityEngine.UIModule'){$refs+='-r:'+"$g/interop/$dll.dll"}
foreach($dll in 'BepInEx.Core','BepInEx.Unity.IL2CPP','0Harmony','Il2CppInterop.Runtime'){$refs+='-r:'+"$g/core/$dll.dll"}
$sources=@(Get-ChildItem -LiteralPath $PSScriptRoot -Filter '*.cs'|ForEach-Object {$_.FullName})
dotnet $Csc -nologo -unsafe -deterministic -optimize+ -target:library -define:FramePacing "-out:$Output" $refs $sources
if($LASTEXITCODE){throw 'Model support build failed'}
