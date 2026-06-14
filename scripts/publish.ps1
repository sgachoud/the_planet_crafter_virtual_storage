param(
    [Parameter(Mandatory)]
    [ValidateSet('Debug','Release')]
    [System.String]$Target,

    [Parameter(Mandatory)]
    [System.String]$TargetPath,

    [Parameter(Mandatory)]
    [System.String]$TargetAssembly,

    [Parameter(Mandatory)]
    [System.String]$PlanetCrafterPath,

    [Parameter(Mandatory)]
    [System.String]$ProjectPath,

    [System.String]$DeployPath
)

# Make sure Get-Location is the script path
Push-Location -Path (Split-Path -Parent $MyInvocation.MyCommand.Path)

# Test some preliminaries
("$TargetPath", "$PlanetCrafterPath") | % {
    if (!(Test-Path "$_")) {Write-Error -ErrorAction Stop -Message "$_ folder is missing"}
}

# Plugin name without ".dll"
$name = "$TargetAssembly" -Replace('.dll')

# Main Script
Write-Host "Publishing for $Target from $TargetPath"

if ($Target.Equals("Debug")) {
    if ($DeployPath.Equals("")) {
        $DeployPath = "$PlanetCrafterPath\BepInEx\plugins"
    }

    $plug = New-Item -Type Directory -Path "$DeployPath\$name" -Force
    Write-Host "Copy $TargetAssembly to $plug"
    Copy-Item -Path "$TargetPath\$name.dll" -Destination "$plug" -Force

    if (Test-Path -Path "$TargetPath\$name.pdb") {
        Copy-Item -Path "$TargetPath\$name.pdb" -Destination "$plug" -Force
    }
}

if ($Target.Equals("Release")) {
    Write-Host "Packaging for ThunderStore..."
    $Package = "Package"
    $PackagePath = "$ProjectPath\$Package"

    New-Item -Type Directory -Path "$PackagePath\plugins" -Force
    Copy-Item -Path "$TargetPath\$TargetAssembly" -Destination "$PackagePath\plugins\$TargetAssembly" -Force
    Copy-Item -Path "$ProjectPath\README.md" -Destination "$PackagePath\README.md" -Force
    Compress-Archive -Path "$PackagePath\*" -DestinationPath "$TargetPath\$TargetAssembly.zip" -Force
}

Pop-Location
