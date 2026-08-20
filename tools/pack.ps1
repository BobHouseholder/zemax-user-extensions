# Assemble a drag/drop install zip from the built add-ins.
#
# Produces dist\zemax-user-extensions-<release>.zip, laid out to mirror
# {Zemax Data}\ZOS-API\ so that one drag of the ZOS-API folder onto the Zemax data
# folder lands every add-in in the folder OpticStudio reads:
#
#     INSTALL.txt
#     manifest.txt
#     ZOS-API\Extensions\*.exe        + .exe.config
#     ZOS-API\User Analysis\*.exe     + .exe.config
#
# Run it - the -ExecutionPolicy is not optional on a default Windows box, where
# running a .ps1 from file is disabled outright:
#     powershell -NoProfile -ExecutionPolicy Bypass -File tools\pack.ps1
#
# Build first - this packs what is in bin\Release, it does not compile:
#     Get-ChildItem extensions -Filter *.csproj -Recurse -Depth 1 |
#         ForEach-Object { dotnet build $_.FullName -c Release }
#
# Each project's destination comes from ZemaxDeployKind in its .csproj, the same
# source of truth the DeployToZemax target in ZemaxPaths.props uses, so a project
# added later lands in the right folder with no edit here.
#
# DELIBERATELY EXCLUDED:
#   ZOSAPI_NetHelper.dll  an Ansys binary. Shipping it would redistribute Ansys
#                         code, and it is unnecessary: the OpticStudio installer
#                         already places a copy in both destination folders.
#   *.pdb                 debug symbols, of no use to someone who is not building.
# Two guards below fail the run rather than let either slip in, because the whole
# licence position rests on the zip containing nothing but our own IL.
#
# See the Releases section of the README before publishing anything this produces.
# The repository's stated position is that no binary is published; this script
# exists so that cutting one is a repeatable operation rather than an improvised
# one, not because the position has changed.

[CmdletBinding()]
param(
  # Repo root. Defaults to the parent of tools\, resolved in the body - NOT here:
  # $PSScriptRoot is empty while param() defaults are evaluated under Windows
  # PowerShell 5.1, which collapses the path to nothing and fails on the first
  # Split-Path. Leave it unset and let the body work it out.
  [string]$Repo,
  [string]$OutDir,
  # Pack from a dirty tree. Off by default: a published binary that corresponds to
  # no commit cannot be rebuilt or audited later, which is the single worst
  # property a hand-built release can have.
  [switch]$AllowDirty
)

$ErrorActionPreference = "Stop"
if (-not $Repo) {
  $here = if ($PSScriptRoot) { $PSScriptRoot } else { Split-Path -Parent $MyInvocation.MyCommand.Path }
  $Repo = Split-Path -Parent $here
}
if (-not $OutDir) { $OutDir = Join-Path $Repo "dist" }

# --- provenance: which commit, and is the tree clean --------------------------
$commit = "(not a git checkout)"
$dirty = @()
if (Test-Path (Join-Path $Repo ".git")) {
  $commit = (& git -C $Repo rev-parse HEAD).Trim()
  $dirty  = @(& git -C $Repo status --porcelain -- extensions ZemaxPaths.props | Where-Object { $_ })
}
if ($dirty.Count -gt 0) {
  $listing = ($dirty | ForEach-Object { "    $_" }) -join "`n"
  if (-not $AllowDirty) {
    throw ("Working tree is dirty; the zip would correspond to no commit:`n$listing`n" +
           "  Commit first, or pass -AllowDirty for a throwaway build.")
  }
  Write-Warning "packing from a DIRTY tree - not reproducible from any commit:`n$listing"
  $commit += " (+ uncommitted changes)"
}

# --- the OpticStudio these were compiled against ------------------------------
$install = Get-ChildItem "C:\Program Files" -Directory -ErrorAction SilentlyContinue |
           Where-Object { $_.Name -like "Ansys Zemax OpticStudio*" } |
           Sort-Object Name -Descending | Select-Object -First 1
if (-not $install) { throw "No 'Ansys Zemax OpticStudio*' install found under C:\Program Files." }
$osVer  = (Get-Item (Join-Path $install.FullName "OpticStudio.exe")).VersionInfo.ProductVersion
$osName = $install.Name -replace '^Ansys Zemax OpticStudio ',''

# --- stage --------------------------------------------------------------------
# Staged OUTSIDE the repo, deliberately. A checkout sitting in a file-sync folder
# - Dropbox, OneDrive, Drive - gets each .exe opened for indexing the moment it
# appears, and staging under dist\ then killed Compress-Archive with "being used
# by another process": intermittent, and pointing at nothing the caller did.
# Only the finished .zip is written back into the working tree.
$stage = Join-Path ([IO.Path]::GetTempPath()) ("zue-pack-" + [Guid]::NewGuid().ToString("N"))
New-Item -ItemType Directory -Path $stage -Force | Out-Null
New-Item -ItemType Directory -Path $OutDir -Force | Out-Null

$rows = @()
foreach ($proj in (Get-ChildItem (Join-Path $Repo "extensions") -Filter *.csproj -Recurse -Depth 1)) {
  $name = [IO.Path]::GetFileNameWithoutExtension($proj.Name)
  $xml  = [xml](Get-Content $proj.FullName)
  $kind = ($xml.Project.PropertyGroup.ZemaxDeployKind | Where-Object { $_ }) -join ''
  if ([string]::IsNullOrWhiteSpace($kind)) { $kind = "Extensions" }

  $exe = Join-Path (Split-Path $proj.FullName) "bin\Release\$name.exe"
  if (-not (Test-Path $exe)) { Write-Warning "$name - no build output, skipped"; continue }

  $dest = Join-Path $stage "ZOS-API\$kind"
  New-Item -ItemType Directory -Path $dest -Force | Out-Null
  Copy-Item $exe $dest
  if (Test-Path "$exe.config") { Copy-Item "$exe.config" $dest }

  $rows += [pscustomobject]@{
    Name = $name; Kind = $kind
    Size = (Get-Item $exe).Length
    Sha  = (Get-FileHash $exe -Algorithm SHA256).Hash
  }
}
if ($rows.Count -eq 0) { throw "Nothing staged - build the projects first." }

# --- guards: nothing that is not ours may ship --------------------------------
$stray = Get-ChildItem $stage -Recurse -File | Where-Object { $_.Extension -notin @('.exe','.config') }
if ($stray) { throw "non-shippable files staged: $($stray.Name -join ', ')" }
$ansys = Get-ChildItem $stage -Recurse -File -Filter "ZOSAPI*"
if ($ansys) { throw "Ansys binary staged: $($ansys.Name -join ', ')" }

# A shipped .exe must not name the machine that built it. .NET stamps the
# absolute .pdb path into the PE debug directory unless DebugType is none, which
# ZemaxPaths.props sets for Release - this re-checks the actual bytes rather than
# trusting that setting, because the leak is invisible in any file listing and
# permanent once published.
foreach ($f in (Get-ChildItem $stage -Recurse -File -Filter *.exe)) {
  $text = [Text.Encoding]::ASCII.GetString([IO.File]::ReadAllBytes($f.FullName))
  $found = [regex]::Matches($text, '[A-Za-z]:\\Users\\[!-~]{0,120}|[!-~]{0,120}\.pdb')
  if ($found.Count -gt 0) {
    throw ("$($f.Name) carries a build-machine path: '" + $found[0].Value + "'. " +
           "Build Release with DebugType=none (see ZemaxPaths.props) and re-run.")
  }
}

# --- manifest -----------------------------------------------------------------
$m = @(
  "Zemax OpticStudio user add-ins"
  "https://github.com/BobHouseholder/zemax-user-extensions"
  ""
  "Source commit : $commit"
  "Built against : Ansys Zemax OpticStudio $osName (OpticStudio.exe $osVer)"
  "Framework     : .NET Framework 4.8, x64"
  ""
  "These were compiled against the OpticStudio above. The ZOS-API assemblies are"
  "referenced but not redistributed, and are resolved at run time against YOUR"
  "installation - so a different OpticStudio release will load these without"
  "complaint, and can still fail if a member they call has been withdrawn."
  ""
  "SHA-256:"
)
foreach ($r in ($rows | Sort-Object Kind, Name)) {
  $m += ("  {0}  {1}  ({2}, {3:N0} bytes)" -f $r.Sha, "$($r.Name).exe", $r.Kind, $r.Size)
}
$m -join "`r`n" | Set-Content (Join-Path $stage "manifest.txt") -Encoding utf8

# --- install instructions -------------------------------------------------------
@"
INSTALL
=======

These are compiled add-ins for Ansys Zemax OpticStudio, built against $osName.
They are NOT signed, and Windows treats downloaded executables as untrusted - see
UNBLOCK below, which you will probably need.

READ THIS FIRST IF YOU HAVE INSTALLED AN ANSYS EXTENSION BEFORE
--------------------------------------------------------------
Ansys's own extension zips - the CODE V Converter, for instance - are extracted
INTO the Extensions folder. Do not do that with this one. This zip carries TWO
destinations, because one of the nine is a User Analysis rather than an
extension, so its top level is a ZOS-API folder rather than loose .exe files.
Extracting it into Extensions would give you

    ...\Zemax\ZOS-API\Extensions\ZOS-API\Extensions\*.exe

which is nested one level too deep, and nothing would appear in any menu.
Follow step 2 instead.

1. Close OpticStudio, and find your Zemax data folder. OpticStudio reports it
   under Setup > Project Preferences > Folders. It is usually
       C:\Users\<you>\Documents\Zemax
   If your Documents folder is redirected to OneDrive you may have two folders
   that look alike. Use the one OpticStudio names, not the one that looks right.

2. Drag the ZOS-API folder out of this zip onto your Zemax data folder - onto
   the folder ABOVE, not into Extensions. Windows asks to merge - say yes.
   Nothing of yours is replaced except an add-in of the same name.

3. Do NOT double-click these from Explorer. OpticStudio launches them, you do not.
   A double-click ends in a connection error - harmless, but some of them show a
   settings dialog first, which makes it look as though it worked.

4. Open OpticStudio. The extensions are under Programming > User Extensions, and
   AthermalAnalysis - the one User Analysis - under Analyze > User Analysis.
   If you left OpticStudio open at step 1, Programming > Refresh List picks up
   new extensions without a restart; a new User Analysis still needs one.

UNBLOCK
-------
Windows marks files that came from the internet, and an unsigned .exe carrying
that mark may be blocked. To clear it for everything you just copied, in
PowerShell - adjusting the path to your Zemax data folder:

    Get-ChildItem "`$env:USERPROFILE\Documents\Zemax\ZOS-API" -Recurse -Filter *.exe | Unblock-File

Or right-click each .exe, Properties, tick Unblock, OK.

CANCEL DOES NOT WORK EVERYWHERE
-------------------------------
AthermalScan, DetectorDump, EquivalentGlassFinder and LayoutRender poll for the
Terminate button and stop at the next iteration. CryoGlass, DistortionTarget,
MoldStress, ReverseSystem and the AthermalAnalysis window do not - Cancel does
nothing there and the run continues to completion.

BUILDING INSTEAD
----------------
Preferable, and one command: it compiles against YOUR OpticStudio rather than the
one above, and deploys itself. See the Building section of the README.

WHAT IS NOT IN HERE
-------------------
ZOSAPI_NetHelper.dll is an Ansys file and is not redistributed. Your OpticStudio
installation already places a copy in both destination folders, which is the copy
these add-ins load.
"@ | Set-Content (Join-Path $stage "INSTALL.txt") -Encoding utf8

# --- zip --------------------------------------------------------------------------
$zip = Join-Path $OutDir ("zemax-user-extensions-" + ($osName -replace ' ','') + ".zip")
if (Test-Path $zip) { Remove-Item $zip -Force }
Compress-Archive -Path (Join-Path $stage '*') -DestinationPath $zip -CompressionLevel Optimal
Remove-Item $stage -Recurse -Force

""
"zip     : $zip"
"size    : {0:N0} bytes" -f (Get-Item $zip).Length
"commit  : $commit"
"against : OpticStudio $osName"
$rows | Sort-Object Kind, Name | Format-Table Name, Kind, Size -AutoSize
