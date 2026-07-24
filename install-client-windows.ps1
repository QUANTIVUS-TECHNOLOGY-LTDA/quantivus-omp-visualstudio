[CmdletBinding(SupportsShouldProcess)]
param(
    [Parameter(Mandatory)]
    [ValidateNotNullOrEmpty()]
    [string]$InstallBlob,
    [string]$SourceDirectory = $PSScriptRoot,
    [string]$InstallDirectory = (Join-Path $env:ProgramFiles "QMCPProxy\Client"),
    [string]$DataDirectory = (Join-Path $env:ProgramData "QMCPProxy\Client"),
    [string]$ServiceName = "QMCPProxyClient"
)

Set-StrictMode -Version Latest
$ErrorActionPreference = "Stop"

function Assert-Administrator {
    $identity = [Security.Principal.WindowsIdentity]::GetCurrent()
    $principal = [Security.Principal.WindowsPrincipal]::new($identity)
    if (-not $principal.IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
        throw "Run this installer from an elevated PowerShell session."
    }
}

function Test-ServiceExists([string]$Name) {
    return $null -ne (Get-Service -Name $Name -ErrorAction SilentlyContinue)
}


$source = [IO.Path]::GetFullPath($SourceDirectory)
$install = [IO.Path]::GetFullPath($InstallDirectory)
$data = [IO.Path]::GetFullPath($DataDirectory)
$sourceExecutable = Join-Path $source "QMCPProxy.Client.Windows.exe"
$targetExecutable = Join-Path $install "QMCPProxy.Client.Windows.exe"
$payloadFile = Join-Path $data "client.install"
$markerFile = Join-Path $data "installed.json"
$logDirectory = Join-Path $data "Logs"

if (-not (Test-Path -LiteralPath $sourceExecutable -PathType Leaf)) {
    throw "Client executable not found: $sourceExecutable"
}
if (Test-ServiceExists $ServiceName) {
    throw "QMCPProxy is already installed on this computer as service '$ServiceName'. Run uninstall-client-windows.ps1 before reinstalling."
}
if ((Test-Path -LiteralPath $markerFile -PathType Leaf) -or
    (Test-Path -LiteralPath $payloadFile -PathType Leaf) -or
    (Test-Path -LiteralPath $install)) {
    throw "A QMCPProxy client installation already exists on this computer. Run uninstall-client-windows.ps1 before reinstalling."
}
if (Get-Process -Name "QMCPProxy.Client.Windows" -ErrorAction SilentlyContinue) {
    throw "QMCPProxy.Client.Windows is already running on this computer. Stop and uninstall it before installing another client."
}
if ($source.TrimEnd('\') -eq $install.TrimEnd('\')) {
    throw "SourceDirectory and InstallDirectory must be different."
}

Assert-Administrator

$utf8WithoutBom = [Text.UTF8Encoding]::new($false)
$validationFile = [IO.Path]::GetTempFileName()
try {
    [IO.File]::WriteAllText($validationFile, $InstallBlob.Trim(), $utf8WithoutBom)
    & $sourceExecutable --install-file $validationFile --version-info | Out-Null
    if ($LASTEXITCODE -ne 0) {
        throw "The portal install blob is invalid."
    }
}
finally {
    Remove-Item -LiteralPath $validationFile -Force -ErrorAction SilentlyContinue
}

$serviceCreated = $false
if ($PSCmdlet.ShouldProcess($install, "Install QMCPProxy Windows client service")) {
    try {
        New-Item -ItemType Directory -Force -Path $install, $data, $logDirectory | Out-Null
        Copy-Item -Path (Join-Path $source "*") -Destination $install -Recurse -Force
        if (-not (Test-Path -LiteralPath $targetExecutable -PathType Leaf)) {
            throw "Installed executable not found after copy: $targetExecutable"
        }

        [IO.File]::WriteAllText($payloadFile, $InstallBlob.Trim(), $utf8WithoutBom)
        $marker = @{
            serviceName = $ServiceName
            installedAt = [DateTimeOffset]::UtcNow.ToString("O")
            executable = $targetExecutable
        } | ConvertTo-Json -Compress
        [IO.File]::WriteAllText($markerFile, $marker, $utf8WithoutBom)

        & icacls.exe $data /inheritance:r /grant:r '*S-1-5-18:(OI)(CI)F' '*S-1-5-32-544:(OI)(CI)F' /Q | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to protect the client configuration directory."
        }

        $binaryPath = ('"{0}" --service --install-file "{1}"' -f $targetExecutable, $payloadFile)
        New-Service -Name $ServiceName -BinaryPathName $binaryPath -DisplayName "QMCPProxy Client" -Description "Outbound QMCPProxy connector" -StartupType Automatic | Out-Null
        $serviceCreated = $true
        & sc.exe failure $ServiceName "reset= 86400" "actions= restart/5000/restart/15000/restart/60000" | Out-Null
        if ($LASTEXITCODE -ne 0) {
            throw "Failed to configure service recovery."
        }
        $serviceRegistryPath = "HKLM:\SYSTEM\CurrentControlSet\Services\$ServiceName"
        New-ItemProperty -Path $serviceRegistryPath -Name Environment -PropertyType MultiString -Value @("QMCP__LogDirectory=$logDirectory") -Force | Out-Null

        Start-Service -Name $ServiceName
        (Get-Service -Name $ServiceName).WaitForStatus([ServiceProcess.ServiceControllerStatus]::Running, [TimeSpan]::FromSeconds(30))
        Write-Host "QMCPProxy Client was installed once for this computer and is running as service '$ServiceName'."
        Write-Host "Uninstall: run uninstall-client-windows.ps1 from an elevated PowerShell session."
    }
    catch {
        if ($serviceCreated) {
            Stop-Service -Name $ServiceName -Force -ErrorAction SilentlyContinue
            & sc.exe delete $ServiceName | Out-Null
        }
        Remove-Item -LiteralPath $install, $data -Recurse -Force -ErrorAction SilentlyContinue
        throw
    }
}
