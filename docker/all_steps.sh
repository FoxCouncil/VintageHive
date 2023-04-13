#!/bin/bash
rootDir=$(pwd)

echo "Step 1: Dockerizing..."
./step1_dockerize.sh
echo "Step 1 completed."
cd "$rootDir"

echo "Step 2: Creating volumes..."
./step2_volumes.sh
echo "Step 2 completed."
cd "$rootDir"

echo "Step 3: Running..."
./step3_run.sh
echo "Step 3 completed."
cd "$rootDir"
