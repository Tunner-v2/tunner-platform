Set-StrictMode -Version Latest

function Test-TunnerRepositoryRoot {
    param([Parameter(Mandatory = $true)][string]$Candidate)
    $full = [IO.Path]::GetFullPath($Candidate)
    return (Test-Path -LiteralPath $full -PathType Container) -and (Test-Path -LiteralPath (Join-Path $full '.tunner-root') -PathType Leaf)
}

function Resolve-TunnerRepositoryRoot {
    param([string]$RepositoryRoot, [string]$StartDirectory = (Get-Location).Path)
    if (-not [string]::IsNullOrWhiteSpace($RepositoryRoot)) {
        if (Test-TunnerRepositoryRoot $RepositoryRoot) { return (Resolve-Path -LiteralPath $RepositoryRoot).Path }
        throw '-RepositoryRoot must identify a directory containing .tunner-root.'
    }
    if (-not [string]::IsNullOrWhiteSpace($env:TUNNER_REPO_ROOT)) {
        if (Test-TunnerRepositoryRoot $env:TUNNER_REPO_ROOT) { return (Resolve-Path -LiteralPath $env:TUNNER_REPO_ROOT).Path }
        throw 'TUNNER_REPO_ROOT must identify a directory containing .tunner-root.'
    }
    try {
        $gitRoot = (& git -C $StartDirectory rev-parse --show-toplevel 2>$null).Trim()
        if ($LASTEXITCODE -eq 0 -and (Test-TunnerRepositoryRoot $gitRoot)) { return (Resolve-Path -LiteralPath $gitRoot).Path }
    } catch { }
    for ($current = [IO.DirectoryInfo]([IO.Path]::GetFullPath($StartDirectory)); $null -ne $current; $current = $current.Parent) {
        if (Test-TunnerRepositoryRoot $current.FullName) { return $current.FullName }
    }
    throw 'Unable to resolve the Tunner repository root. Supply -RepositoryRoot, set TUNNER_REPO_ROOT, run inside a Git checkout, or ensure an ancestor contains .tunner-root.'
}

Export-ModuleMember -Function Resolve-TunnerRepositoryRoot