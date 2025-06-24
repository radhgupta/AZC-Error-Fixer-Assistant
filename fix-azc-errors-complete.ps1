# Complete AZC Error Fixing Workflow Script
# This script handles the entire flow from TypeSpec compilation to AI-powered error fixing

param(
    [switch]$SkipRecompile = $false
)

$ErrorActionPreference = "Stop"
$scriptRoot = $PSScriptRoot

Write-Host "=== Starting Complete AZC Error Fixing Workflow ===" -ForegroundColor Green

# Step 1: Compile TypeSpec
Write-Host "`n[Step 1] Compiling TypeSpec..." -ForegroundColor Yellow
# try {
#     Set-Location $scriptRoot
    
#     # Try different methods to run TypeSpec compiler
#     $tspCompiled = $false
    
#     # Method 1: Try npx (if Node.js is installed)
#     try {
#         Write-Host "Attempting to compile with npx @typespec/compiler..." -ForegroundColor Cyan
#         npx tsp compile src/main.tsp --output-dir tsp-output
#         $tspCompiled = $true
#         Write-Host "TypeSpec compilation with npx completed successfully!" -ForegroundColor Green
#     }
#     catch {
#         Write-Host "npx method failed, trying alternative..." -ForegroundColor Yellow
#     }
    
#     # Method 2: Try tsp command directly (if in PATH)
#     if (-not $tspCompiled) {
#         try {
#             Write-Host "Attempting to compile with tsp command..." -ForegroundColor Cyan
#             tsp compile src/main.tsp --output-dir tsp-output
#             $tspCompiled = $true
#             Write-Host "TypeSpec compilation completed successfully!" -ForegroundColor Green
#         }
#         catch {
#             Write-Host "Direct tsp command failed..." -ForegroundColor Yellow
#         }
#     }
    
#     # Method 3: Try local node_modules (if TypeSpec is installed locally)
#     if (-not $tspCompiled) {
#         $localTsp = ".\node_modules\.bin\tsp.cmd"
#         if (Test-Path $localTsp) {
#             Write-Host "Attempting to compile with local TypeSpec installation..." -ForegroundColor Cyan
#             & $localTsp compile src/main.tsp --output-dir tsp-output
#             $tspCompiled = $true
#             Write-Host "TypeSpec compilation completed successfully!" -ForegroundColor Green
#         }
#     }
    
#     if (-not $tspCompiled) {
#         throw "Could not find TypeSpec compiler. Please ensure TypeSpec is installed via 'npm install -g @typespec/compiler' or 'npm install @typespec/compiler'"
#     }
# }
# catch {
#     Write-Host "Error during TypeSpec compilation: $_" -ForegroundColor Red
#     exit 1
# }

# Step 2: Capture AZC errors
Write-Host "`n[Step 2] Capturing AZC errors..." -ForegroundColor Yellow
try {
    # Ensure log directory exists
    $logDir = Join-Path $scriptRoot "log"
    if (-not (Test-Path $logDir)) {
        New-Item -ItemType Directory -Path $logDir | Out-Null
    }

    # Change to generated C# src folder and build
    Set-Location "$scriptRoot\tsp-output\@azure-tools\typespec-csharp\src"
    dotnet build --no-incremental | Tee-Object -FilePath "$logDir\build-output.log"    # Extract AZC errors
    $azcErrors = Select-String -Path "$logDir\build-output.log" -Pattern "AZC\d{4}"
    if ($azcErrors) {
        $azcErrors | ForEach-Object { $_.Line } | Set-Content "$logDir\azc-errors.txt"
        Write-Host "Found $($azcErrors.Count) AZC errors/warnings" -ForegroundColor Yellow
        Write-Host "AZC errors saved to $logDir\azc-errors.txt" -ForegroundColor Green
        
        # Show the AZC errors for reference
        Write-Host "`nAZC Errors found:" -ForegroundColor Yellow
        $azcErrors | ForEach-Object { Write-Host "  $($_.Line)" -ForegroundColor Red }
    } else {
        Write-Host "No AZC errors found! Your code is clean." -ForegroundColor Green
        Write-Host "Note: Other C# compilation errors may exist, but they are not AZC analyzer issues." -ForegroundColor Cyan
        Write-Host "Workflow completed successfully - no AZC fixes needed." -ForegroundColor Green
        exit 0
    }
}
catch {
    Write-Host "Error during AZC error capture: $_" -ForegroundColor Red
    exit 1
}

# Step 3: Run AI Agent to fix errors
Write-Host "`n[Step 3] Running AI Agent to analyze and fix errors..." -ForegroundColor Yellow
try {
    Set-Location "$scriptRoot\azc-error-fixing\AZC0030\AzcAnalyzerFixer"
    
    # Build the analyzer fixer if needed
    Write-Host "Building AZC Analyzer Fixer..." -ForegroundColor Cyan
    dotnet build --verbosity quiet
    
    # Run the AI agent
    Write-Host "Starting AI analysis..." -ForegroundColor Cyan
    dotnet run --verbosity quiet
    
    Write-Host "AI Agent analysis completed!" -ForegroundColor Green
}
catch {
    Write-Host "Error during AI Agent execution: $_" -ForegroundColor Red
    exit 1
}

# # Step 4: Optional recompilation to verify fixes
# if (-not $SkipRecompile) {
#     Write-Host "`n[Step 4] Recompiling to verify fixes..." -ForegroundColor Yellow
#     try {        # Recompile TypeSpec
#         Set-Location $scriptRoot
#         Write-Host "Recompiling TypeSpec..." -ForegroundColor Cyan
        
#         # Use the same compilation logic as Step 1
#         $recompiled = $false
#         try {
#             npx @typespec/compiler compile src/main.tsp --output-dir tsp-output
#             $recompiled = $true
#         }
#         catch {
#             try {
#                 tsp compile src/main.tsp --output-dir tsp-output
#                 $recompiled = $true
#             }
#             catch {
#                 $localTsp = ".\node_modules\.bin\tsp.cmd"
#                 if (Test-Path $localTsp) {
#                     & $localTsp compile src/main.tsp --output-dir tsp-output
#                     $recompiled = $true
#                 }
#             }
#         }
        
#         if (-not $recompiled) {
#             throw "Could not recompile TypeSpec"
#         }
        
#         # Check for remaining AZC errors
#         Set-Location "$scriptRoot\tsp-output\@azure-tools\typespec-csharp\src"
#         $verifyOutput = dotnet build --no-incremental 2>&1
#         $remainingErrors = $verifyOutput | Select-String -Pattern "AZC\d{4}"
        
#         if ($remainingErrors) {
#             Write-Host "Warning: $($remainingErrors.Count) AZC errors still remain:" -ForegroundColor Yellow
#             $remainingErrors | ForEach-Object { Write-Host "  $_" -ForegroundColor Yellow }
#             Write-Host "You may need to run the workflow again or manual intervention may be required." -ForegroundColor Yellow
#         } else {
#             Write-Host "Success! All AZC errors have been fixed!" -ForegroundColor Green
#         }
#     }
#     catch {
#         Write-Host "Error during verification recompilation: $_" -ForegroundColor Red
#         Write-Host "The fixes may have been applied, but verification failed." -ForegroundColor Yellow
#     }
# } else {
#     Write-Host "`nSkipping recompilation verification (use -SkipRecompile:$false to enable)" -ForegroundColor Cyan
# }

Write-Host "`n=== Workflow Completed ===" -ForegroundColor Green
Set-Location $scriptRoot
