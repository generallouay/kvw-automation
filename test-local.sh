#!/bin/bash

# Local Testing Script for Klik voor Wonen Automation

echo "=== Klik voor Wonen - Local Testing ==="
echo ""

# Prompt for credentials
read -p "Enter your Klik voor Wonen username: " USERNAME
read -sp "Enter your Klik voor Wonen password: " PASSWORD
echo ""

if [ -z "$USERNAME" ] || [ -z "$PASSWORD" ]; then
    echo "ERROR: Username and password are required!"
    exit 1
fi

# Set environment variables
export KLIKVOORWONEN_USERNAME="$USERNAME"
export KLIKVOORWONEN_PASSWORD="$PASSWORD"
export HEADLESS="false"  # Show browser for testing

echo ""
echo "Step 1: Restoring dependencies..."
dotnet restore

echo ""
echo "Step 2: Building project..."
dotnet build

echo ""
echo "Step 3: Installing Playwright browsers..."
powershell bin/Debug/net8.0/playwright.ps1 install chromium

echo ""
echo "Step 4: Running automation (browser will be visible)..."
echo ""
dotnet run

echo ""
echo "=== Test Complete ==="
read -p "Press Enter to close..."
