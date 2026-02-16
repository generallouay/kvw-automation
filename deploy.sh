#!/bin/bash

# Klik voor Wonen - Azure Deployment Script
# This script handles the complete deployment to Azure Container Apps

set -e  # Exit on error

echo "=== Klik voor Wonen Automation - Azure Deployment ==="
echo ""

# Check if Azure CLI is installed
if ! command -v az &> /dev/null; then
    echo "ERROR: Azure CLI is not installed. Please install it first."
    echo "Visit: https://docs.microsoft.com/en-us/cli/azure/install-azure-cli"
    exit 1
fi

# Variables
RESOURCE_GROUP="rg-klikvoorwonen"
LOCATION="westeurope"
ACR_NAME="acrklikvoorwonen"  # Change this if needed - must be globally unique
ENVIRONMENT_NAME="cae-klikvoorwonen"
APP_NAME="ca-klikvoorwonen-reactor"
IMAGE_NAME="klikvoorwonen-reactor"
CRON_SCHEDULE="0 23 * * *"  # 11:00 PM daily

echo "Configuration:"
echo "  Resource Group: $RESOURCE_GROUP"
echo "  Location: $LOCATION"
echo "  ACR Name: $ACR_NAME"
echo "  Schedule: $CRON_SCHEDULE"
echo ""

# Prompt for credentials
read -p "Enter your Klik voor Wonen username: " USERNAME
read -sp "Enter your Klik voor Wonen password: " PASSWORD
echo ""

if [ -z "$USERNAME" ] || [ -z "$PASSWORD" ]; then
    echo "ERROR: Username and password are required!"
    exit 1
fi

# Login to Azure
echo ""
echo "Step 1: Logging in to Azure..."
az login

# Create or verify resource group exists
echo ""
echo "Step 2: Verifying resource group..."
if az group show --name $RESOURCE_GROUP &> /dev/null; then
    echo "✓ Resource group exists"
else
    echo "Creating resource group..."
    az group create --name $RESOURCE_GROUP --location $LOCATION
fi

# Create ACR
echo ""
echo "Step 3: Creating Azure Container Registry..."
if az acr show --name $ACR_NAME --resource-group $RESOURCE_GROUP &> /dev/null; then
    echo "✓ ACR already exists"
else
    az acr create \
      --resource-group $RESOURCE_GROUP \
      --name $ACR_NAME \
      --sku Basic \
      --admin-enabled true
fi

# Build and push image
echo ""
echo "Step 4: Building and pushing Docker image..."
az acr build \
  --registry $ACR_NAME \
  --image $IMAGE_NAME:latest \
  --file Dockerfile \
  .

# Create Container Apps Environment
echo ""
echo "Step 5: Creating Container Apps Environment..."
if az containerapp env show --name $ENVIRONMENT_NAME --resource-group $RESOURCE_GROUP &> /dev/null; then
    echo "✓ Environment already exists"
else
    az containerapp env create \
      --name $ENVIRONMENT_NAME \
      --resource-group $RESOURCE_GROUP \
      --location $LOCATION
fi

# Get ACR credentials
echo ""
echo "Step 6: Retrieving ACR credentials..."
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query username -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv)

# Create or update Container App Job
echo ""
echo "Step 7: Creating/Updating Container App Job..."
if az containerapp job show --name $APP_NAME --resource-group $RESOURCE_GROUP &> /dev/null; then
    echo "Job exists, updating..."
    az containerapp job update \
      --name $APP_NAME \
      --resource-group $RESOURCE_GROUP \
      --image $ACR_NAME.azurecr.io/$IMAGE_NAME:latest \
      --secrets \
        klikvoorwonen-username="$USERNAME" \
        klikvoorwonen-password="$PASSWORD"
else
    echo "Creating new job..."
    az containerapp job create \
      --name $APP_NAME \
      --resource-group $RESOURCE_GROUP \
      --environment $ENVIRONMENT_NAME \
      --trigger-type "Schedule" \
      --cron-expression "$CRON_SCHEDULE" \
      --replica-timeout 1800 \
      --replica-retry-limit 1 \
      --parallelism 1 \
      --replica-completion-count 1 \
      --image $ACR_NAME.azurecr.io/$IMAGE_NAME:latest \
      --cpu 0.5 \
      --memory 1Gi \
      --registry-server $ACR_NAME.azurecr.io \
      --registry-username $ACR_USERNAME \
      --registry-password $ACR_PASSWORD \
      --secrets \
        klikvoorwonen-username="$USERNAME" \
        klikvoorwonen-password="$PASSWORD" \
      --env-vars \
        KLIKVOORWONEN_USERNAME=secretref:klikvoorwonen-username \
        KLIKVOORWONEN_PASSWORD=secretref:klikvoorwonen-password \
        HEADLESS=true
fi

echo ""
echo "=== Deployment Complete! ==="
echo ""
echo "Your automation is now scheduled to run daily at 9:00 AM."
echo ""
echo "To test it now, run:"
echo "  az containerapp job start --name $APP_NAME --resource-group $RESOURCE_GROUP"
echo ""
echo "To view logs:"
echo "  az containerapp job execution list --name $APP_NAME --resource-group $RESOURCE_GROUP --output table"
echo ""
echo "To update the code in the future:"
echo "  1. Make your changes"
echo "  2. Run this script again"
echo ""
