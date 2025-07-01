#!/usr/bin/env pwsh
#-------------------------------------------------------------------------------
# <copyright file="run-benchmarks.ps1" company="Microsoft Corp.">
#     Copyright (c) Microsoft Corp. All rights reserved.
# </copyright>
#-------------------------------------------------------------------------------

param(
    [Parameter(HelpMessage="Run concurrent benchmarks instead of sequential")]
    [switch]$Concurrent,
    
    [Parameter(HelpMessage="Filter benchmarks by name pattern")]
    [string]$Filter = "",
    
    [Parameter(HelpMessage="Specific provider to benchmark")]
    [ValidateSet("", "InMemory", "FileSystem", "Esent", "ClusterRegistry")]
    [string]$Provider = "",
    
    [Parameter(HelpMessage="Export results to markdown")]
    [switch]$ExportMarkdown,
    
    [Parameter(HelpMessage="Export results to HTML")]
    [switch]$ExportHtml
)

Write-Host "ReliableStore Benchmark Runner" -ForegroundColor Cyan
Write-Host "==============================" -ForegroundColor Cyan

# Build in Release mode
Write-Host "`nBuilding solution in Release mode..." -ForegroundColor Yellow
dotnet build -c Release

if ($LASTEXITCODE -ne 0) {
    Write-Error "Build failed. Exiting."
    exit 1
}

# Navigate to benchmark project
Push-Location "src/Common.Persistence.Benchmarks"

try {
    # Build benchmark arguments
    $benchmarkArgs = @()
    
    if ($Concurrent) {
        $benchmarkArgs += "--"
        $benchmarkArgs += "--concurrent"
    }
    
    if ($Filter) {
        $benchmarkArgs += "--filter"
        $benchmarkArgs += "*$Filter*"
    }
    
    if ($Provider) {
        $benchmarkArgs += "--allCategories"
        $benchmarkArgs += $Provider
    }
    
    if ($ExportMarkdown) {
        $benchmarkArgs += "--exporters"
        $benchmarkArgs += "github"
    }
    
    if ($ExportHtml) {
        $benchmarkArgs += "--exporters"
        $benchmarkArgs += "html"
    }
    
    # Run benchmarks
    if ($Concurrent) {
        Write-Host "`nRunning CONCURRENT benchmarks..." -ForegroundColor Green
    } else {
        Write-Host "`nRunning SEQUENTIAL benchmarks..." -ForegroundColor Green
    }
    
    if ($benchmarkArgs.Count -gt 0) {
        Write-Host "Arguments: $($benchmarkArgs -join ' ')" -ForegroundColor Gray
    }
    
    dotnet run -c Release $benchmarkArgs
    
    if ($LASTEXITCODE -eq 0) {
        Write-Host "`nBenchmarks completed successfully!" -ForegroundColor Green
        Write-Host "Results are available in: BenchmarkDotNet.Artifacts" -ForegroundColor Cyan
    } else {
        Write-Error "Benchmark execution failed."
    }
}
finally {
    Pop-Location
}