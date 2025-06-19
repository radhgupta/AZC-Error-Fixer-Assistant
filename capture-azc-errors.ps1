# Ensure log directory exists at the script level
$logDir = Join-Path $PSScriptRoot "log"
if (-not (Test-Path $logDir)) {
    New-Item -ItemType Directory -Path $logDir | Out-Null
}

# Change directory to the generated C# src folder
Set-Location "$PSScriptRoot\tsp-output\@azure-tools\typespec-csharp\src"

# Run the build and capture output in the log folder
dotnet build --no-incremental | Tee-Object -FilePath "$logDir\build-output.log"

# Extract lines with AZC errors/warnings and save to azc-errors.log in the log folder
Select-String -Path "$logDir\build-output.log" -Pattern "AZC\d{4}" | ForEach-Object { $_.Line } | Set-Content "$logDir\azc-errors.log"

Write-Host "AZC errors and warnings have been saved to $logDir\azc-errors.log"