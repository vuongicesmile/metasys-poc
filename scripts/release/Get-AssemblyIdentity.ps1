param([Parameter(Mandatory)][string]$Path)
$ErrorActionPreference = 'Stop'
[Reflection.AssemblyName]::GetAssemblyName((Resolve-Path -LiteralPath $Path).Path).FullName
