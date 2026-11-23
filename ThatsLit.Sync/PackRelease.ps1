$version = (Select-String -LiteralPath (Join-Path $PSScriptRoot "ThatsLitSyncPlugin.cs") -Pattern 'public const string ModVersion = "([0-9.]+)"').Matches[0].Groups[1].Value
$assembly = Join-Path $PSScriptRoot "bin\Release\net472\ThatsLit.Sync.dll"
$archive = Join-Path $PSScriptRoot "ThatsLitSync_$version.zip"

if (-not (Test-Path -LiteralPath $assembly -PathType Leaf)) {
    throw "Build ThatsLit.Sync in Release configuration before packaging."
}

Bandizip.exe c -root:BepInEx\plugins\ThatsLit $archive $assembly
if ($LASTEXITCODE -ne 0) {
    throw "Bandizip failed with exit code $LASTEXITCODE."
}
