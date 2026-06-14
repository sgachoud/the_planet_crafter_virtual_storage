#!/bin/sh

target="Debug"
targetPath=""
targetAssembly="VirtualStorage.dll"
planetCrafterPath=""
deployPath=""
projectPath=""

while [ "$#" -gt 0 ]; do
  case "$1" in
    --target)
        target="$2"; shift 2 ;;
    --target-path)
        targetPath="$2"; shift 2 ;;
    --target-assembly)
        targetAssembly="$2"; shift 2 ;;
    --planet-crafter-path)
        planetCrafterPath="$2"; shift 2 ;;
    --deploy-path)
        deployPath="$2"; shift 2 ;;
    --project-path)
        projectPath="$2"; shift 2 ;;
    *)
        echo "Warning: Unknown argument $1" >&2; shift ;;
  esac
done

# order of precedence: MOD_DEPLOYPATH > PLANET_CRAFTER_INSTALL > default path
if [ -z "$deployPath" ]; then
    if [ -z "$planetCrafterPath" ]; then
        deployPath="$HOME/.local/share/Steam/steamapps/common/The Planet Crafter/BepInEx/plugins"
    else
        deployPath="$planetCrafterPath/BepInEx/plugins"
    fi
fi

# strip .dll extension
name=$(echo "$targetAssembly" | sed 's/\.dll//')

if [ "$target" = "Debug" ]; then
    plug="$deployPath/$name"
    echo "Copying $targetAssembly to $plug"

    mkdir -p "$plug"
    cp "$targetPath/$targetAssembly" "$plug"
    [ -e "$targetPath/$name.pdb" ] && cp "$targetPath/$name.pdb" "$plug"
fi

if [ "$target" = "Release" ]; then
    packagePath="$projectPath/Package"
    mkdir -p "$packagePath/plugins"
    cp "$targetPath/$targetAssembly" "$packagePath/plugins/"
    cp "$projectPath/README.md" "$packagePath/" 2>/dev/null || true

    if command -v zip > /dev/null; then
        [ -e "$targetPath/$name.zip" ] && rm "$targetPath/$name.zip"
        cd "$packagePath"
        zip -r "$targetPath/$name.zip" . > /dev/null
        echo "Build successful, zip ready at $targetPath/$name.zip"
    else
        echo "Skipping plugin zipping, zip command isn't available."
    fi
fi
