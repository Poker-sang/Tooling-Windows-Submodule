<#
.SYNOPSIS
    Builds toolkit components with specified parameters. Primarily used by maintainers for local testing.

.DESCRIPTION
    This script streamlines building and packing Community Toolkit components with the specified parameters. It allows you to specify the MultiTarget TFM(s) to include or exclude, the WinUI major version to use, the components to build, whether to build samples or source, optional packing when provided with a NupkgOutput, and more. The components can be built in Release configuration and individual (per-component) binlogs can be generated by passing -bl.

.PARAMETER MultiTargets
    Specifies the MultiTarget TFM(s) to include for building the components. The default value is 'all'.

.PARAMETER ExcludeMultiTargets
    Specifies the MultiTarget TFM(s) to exclude for building the components. The default value excludes targets that require additional tooling or workloads to build. Run uno-check to install the required workloads.

.PARAMETER DateForVersion
    Specifies the date for versioning in 'YYMMDD' format. The default value is the current date.

.PARAMETER PreviewVersion
    Specifies the preview version to use if packaging is enabled. Appended with a dash after the version number (formatted Version-PreviewVersion). This parameter is required when NupkgOutput is supplied.

.PARAMETER NupkgOutput
    Specifies the output directory for .nupkg files. This parameter is optional. When supplied, the components will also be packed and nupkg files will be output to the specified directory.

.PARAMETER BinlogOutput
    Specifies the output directory for binlogs. This parameter is optional, default is the current directory.

.PARAMETER EnableBinLogs
    Enables the generation of binlogs by appending '/bl' to the msbuild command. Generated binlogs will match the csproj name. This parameter is optional. Use BinlogOutput to specify the output directory.

.PARAMETER WinUIMajorVersion
    Specifies the WinUI major version to use when building for Uno. Also decides the package id and dependency variant. The default value is '2'.

.PARAMETER Components
    Specifies the names of the components to build. Defaults to all components.

.PARAMETER ExcludeComponents
    Specifies the names of the components to exclude from building. This parameter is optional.

.PARAMETER ComponentDir
    Specifies the directories to build. Defaults to 'src'. Use 'samples' to build the sample projects instead of the source projects.

.PARAMETER AdditionalProperties
  Additional msbuild properties to pass.

.PARAMETER Release
    Specifies whether to build in Release configuration. When specified, it adds /p:Configuration=Release to the msbuild arguments.

.PARAMETER Verbose
    Specifies whether to enable detailed msbuild verbosity. When specified, it adds /v:detailed to the msbuild arguments.

.EXAMPLE
    Build-Toolkit-Components -MultiTargets 'uwp', 'wasm' -DateForVersion '220101' -PreviewVersion 'local' -NupkgOutput 'C:\Output' -BinlogOutput 'C:\Logs' -EnableBinLogs -Components 'MyComponent1', 'MyComponent2' -ExcludeComponents 'MyComponent3' -Release -Verbose

    Builds the 'MyComponent1' and 'MyComponent2' components for the 'uwp' and 'wasm' target frameworks with version '220101' and preview version 'local'. The 'MyComponent3' component will be excluded from building. The .nupkg files will be copied to 'C:\Output' and binlogs will be generated in 'C:\Logs'. The components will be built in Release configuration with detailed msbuild verbosity.

.NOTES
    Author: Arlo Godfrey
    Date:   2/19/2024
#>
Param (
    [ValidateSet('all', 'wasm', 'uwp', 'wasdk', 'wpf', 'linuxgtk', 'macos', 'ios', 'android', 'netstandard')]
    [string[]]$MultiTargets = @('all'),
    
    [ValidateSet('wasm', 'uwp', 'wasdk', 'wpf', 'linuxgtk', 'macos', 'ios', 'android', 'netstandard')]
    [string[]]$ExcludeMultiTargets = @('wpf', 'linuxgtk', 'macos', 'ios', 'android'),

    [string[]]$Components = @("all"),

    [string[]]$ExcludeComponents,

    [string]$DateForVersion = (Get-Date -UFormat %y%m%d),

    [string]$PreviewVersion,

    [string]$NupkgOutput,

    [Alias("bl")]
    [switch]$EnableBinLogs,

    [string]$BinlogOutput,

    [hashtable]$AdditionalProperties,

    [int]$WinUIMajorVersion = 2,

    [string]$ComponentDir = "src",

    [switch]$Release,

    [Alias("v")]
    [switch]$Verbose
)

if ($MultiTargets -eq 'all') {
  $MultiTargets = @('wasm', 'uwp', 'wasdk', 'wpf', 'linuxgtk', 'macos', 'ios', 'android', 'netstandard')
}

if ($ExcludeMultiTargets -eq $null)
{
   $ExcludeMultiTargets = @()
}

$MultiTargets = $MultiTargets | Where-Object { $_ -notin $ExcludeMultiTargets }

if ($Components -eq @('all')) {
    $Components = @('**')
}

if ($ExcludeComponents) {
    $Components = $Components | Where-Object { $_ -notin $ExcludeComponents }
}

# Check if NupkgOutput is supplied without PreviewVersion
if ($NupkgOutput -and -not $PreviewVersion) {
    throw "PreviewVersion is required when NupkgOutput is supplied."
}

# Use the specified MultiTarget TFM and WinUI version
& $PSScriptRoot\MultiTarget\UseTargetFrameworks.ps1 $MultiTargets
& $PSScriptRoot\MultiTarget\UseUnoWinUI.ps1 $WinUIMajorVersion

function Invoke-MSBuildWithBinlog {
    param (
        [string]$TargetHeadPath,
        [switch]$EnableBinLogs,
        [string]$BinlogOutput
    )

    # Reset build args to default
    $msbuildArgs = @("-r", "-m", "/p:DebugType=Portable")

    # Add packing to the msbuild arguments if NupkgOutput is supplied
    if ($NupkgOutput) {
        # Ensure output is relative to $pwd, not to the csproj of each component. 
        $NupkgOutput = (Resolve-Path $NupkgOutput).Path

        $msbuildArgs += "-t:Clean,Build,Pack"
        $msbuildArgs += "/p:PackageOutputPath=$NupkgOutput"
        $msbuildArgs += "/p:DateForVersion=$DateForVersion"
        $msbuildArgs += "/p:PreviewVersion=$PreviewVersion"
    }
    else {
        $msbuildArgs += "-t:Clean,Build"
    }

    # Add additional properties to the msbuild arguments
    if ($AdditionalProperties) {
        foreach ($property in $AdditionalProperties.GetEnumerator()) {
            $msbuildArgs += "/p:$($property.Name)=$($property.Value)"
        }
    }

    # Handle binlog options
    if ($EnableBinLogs) {
        $csprojFileName = [System.IO.Path]::GetFileNameWithoutExtension($TargetHeadPath)
        $defaultBinlogFilename = "$csprojFileName.msbuild.binlog"
        $finalBinlogPath = $defaultBinlogFilename;

        # Set default binlog output location if not provided
        if ($BinlogOutput) {
            $finalBinlogPath = "$BinlogOutput/$defaultBinlogFilename" 
        }

        # Add binlog output path to the msbuild arguments
        $msbuildArgs += "/bl:$finalBinlogPath"
    }

    if ($Release) {
        $msbuildArgs += "/p:Configuration=Release"
    }

    if ($Verbose) {
        $msbuildArgs += "/v:detailed"
    }

    msbuild $msbuildArgs $TargetHeadPath
}

# Components are built individually
foreach ($ComponentName in $Components) {
     # Find all components source csproj (when wildcard), or find specific component csproj by name.
    foreach ($componentCsproj in Get-ChildItem -Path "$PSScriptRoot/../components/$ComponentName/$ComponentDir/*.csproj") {
        # Get component name from csproj path
        $componentPath = Get-Item "$componentCsproj/../../"

        # Get supported MultiTarget for this component
        $supportedMultiTargets = & $PSScriptRoot\MultiTarget\Get-MultiTargets.ps1 -component $($componentPath.BaseName)

        # If this component doesn't list one of the provided MultiTargets, skip it
        if ($MultiTargets -notin $supportedMultiTargets) {
            Write-Warning "Skipping $($componentPath.BaseName), no supported MultiTargets were included for build."
            continue
        }

        Invoke-MSBuildWithBinlog $componentCsproj.FullName $EnableBinLogs $BinlogOutput
    }
}