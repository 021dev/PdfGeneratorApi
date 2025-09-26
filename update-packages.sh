#!/bin/bash

# Script to update .NET packages to latest versions
# Usage: ./update-packages.sh

set -e

echo "🔄 Updating .NET packages to latest versions..."

# Update all packages
dotnet outdated --upgrade

# Restore and build to verify everything works
echo "🔨 Restoring and building..."
dotnet restore
dotnet build

echo "✅ Package update completed successfully!"
echo "📝 Review the changes and test thoroughly before committing."
