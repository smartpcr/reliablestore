#!/usr/bin/env bash
#-------------------------------------------------------------------------------
# <copyright file="run-benchmarks.sh" company="Microsoft Corp.">
#     Copyright (c) Microsoft Corp. All rights reserved.
# </copyright>
#-------------------------------------------------------------------------------

set -euo pipefail

# Colors for output
RED='\033[0;31m'
GREEN='\033[0;32m'
YELLOW='\033[1;33m'
CYAN='\033[0;36m'
NC='\033[0m' # No Color

# Default values
CONCURRENT=false
FILTER=""
PROVIDER=""
EXPORT_MARKDOWN=false
EXPORT_HTML=false

# Parse command line arguments
while [[ $# -gt 0 ]]; do
    case $1 in
        --concurrent)
            CONCURRENT=true
            shift
            ;;
        --filter)
            FILTER="$2"
            shift 2
            ;;
        --provider)
            PROVIDER="$2"
            shift 2
            ;;
        --export-markdown)
            EXPORT_MARKDOWN=true
            shift
            ;;
        --export-html)
            EXPORT_HTML=true
            shift
            ;;
        --help)
            echo "Usage: $0 [OPTIONS]"
            echo ""
            echo "Options:"
            echo "  --concurrent        Run concurrent benchmarks instead of sequential"
            echo "  --filter PATTERN    Filter benchmarks by name pattern"
            echo "  --provider NAME     Specific provider to benchmark (InMemory, FileSystem, Esent, ClusterRegistry)"
            echo "  --export-markdown   Export results to markdown"
            echo "  --export-html       Export results to HTML"
            echo "  --help              Show this help message"
            exit 0
            ;;
        *)
            echo -e "${RED}Unknown option: $1${NC}"
            exit 1
            ;;
    esac
done

echo -e "${CYAN}ReliableStore Benchmark Runner"
echo -e "==============================${NC}"

# Build in Release mode
echo -e "\n${YELLOW}Building solution in Release mode...${NC}"
if ! dotnet build -c Release; then
    echo -e "${RED}Build failed. Exiting.${NC}"
    exit 1
fi

# Navigate to benchmark project
cd src/Common.Persistence.Benchmarks || exit 1

# Build benchmark arguments as an array
BENCHMARK_ARGS=()

if [ "$CONCURRENT" = true ]; then
    BENCHMARK_ARGS+=("--concurrent")
fi

if [ -n "$FILTER" ]; then
    BENCHMARK_ARGS+=("--filter" "*$FILTER*")
fi

if [ -n "$PROVIDER" ]; then
    BENCHMARK_ARGS+=("--allCategories" "$PROVIDER")
fi

if [ "$EXPORT_MARKDOWN" = true ]; then
    BENCHMARK_ARGS+=("--exporters" "github")
fi

if [ "$EXPORT_HTML" = true ]; then
    BENCHMARK_ARGS+=("--exporters" "html")
fi

# Run benchmarks
if [ "$CONCURRENT" = true ]; then
    echo -e "\n${GREEN}Running CONCURRENT benchmarks...${NC}"
else
    echo -e "\n${GREEN}Running SEQUENTIAL benchmarks...${NC}"
fi

if [ "${#BENCHMARK_ARGS[@]}" -gt 0 ]; then
    echo -e "Arguments: ${BENCHMARK_ARGS[*]}"
fi

if dotnet run -c Release -- "${BENCHMARK_ARGS[@]}"; then
    echo -e "\n${GREEN}Benchmarks completed successfully!${NC}"
    echo -e "${CYAN}Results are available in: BenchmarkDotNet.Artifacts${NC}"
else
    echo -e "${RED}Benchmark execution failed.${NC}"
    exit 1
fi