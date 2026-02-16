# Klik voor Wonen Automation

Automated daily reactions to properties on Klik voor Wonen using Playwright and Azure Container Apps.

## 🏗️ Architecture

- **Runtime**: .NET 8.0
- **Browser Automation**: Playwright for .NET
- **Hosting**: Azure Container Apps (Scheduled Job)
- **Container Registry**: Azure Container Registry (ACR)
- **Schedule**: Daily at 9:00 AM

## 📋 Prerequisites

- Azure subscription
- Azure CLI installed
- Docker Desktop installed (for local testing)
- Klik voor Wonen account credentials

## 🚀 Setup Instructions

### Step 1: Test Locally (Important!)

Before deploying to Azure, test the automation locally:

```bash
# Restore dependencies
dotnet restore

# Build the project
dotnet build

# Install Playwright browsers
pwsh bin/Debug/net8.0/playwright.ps1 install

# Set your credentials
export KLIKVOORWONEN_USERNAME="your-username"
export KLIKVOORWONEN_PASSWORD="your-password"
export HEADLESS="false"  # Set to false to see the browser during testing

# Run the automation
dotnet run
```

**⚠️ IMPORTANT:** You'll need to update the CSS selectors in `Program.cs` based on the actual Klik voor Wonen website structure. When you run it locally with `HEADLESS=false`, you can see what's happening and adjust the code accordingly.

### Step 2: Create Azure Resources

```bash
# Login to Azure
az login

# Set variables
RESOURCE_GROUP="rg-klikvoorwonen"
LOCATION="westeurope"
ACR_NAME="acrklikvoorwonen"  # Must be globally unique, only alphanumeric
ENVIRONMENT_NAME="cae-klikvoorwonen"
APP_NAME="ca-klikvoorwonen-reactor"

# The resource group already exists, so skip this:
# az group create --name $RESOURCE_GROUP --location $LOCATION

# Create Azure Container Registry
az acr create \
  --resource-group $RESOURCE_GROUP \
  --name $ACR_NAME \
  --sku Basic \
  --admin-enabled true

# Create Container Apps Environment
az containerapp env create \
  --name $ENVIRONMENT_NAME \
  --resource-group $RESOURCE_GROUP \
  --location $LOCATION
```

### Step 3: Build and Push Docker Image

```bash
# Login to ACR
az acr login --name $ACR_NAME

# Build and push image directly to ACR (easiest method)
az acr build \
  --registry $ACR_NAME \
  --image klikvoorwonen-reactor:latest \
  --file Dockerfile \
  .

# Alternative: Build locally and push
# docker build -t $ACR_NAME.azurecr.io/klikvoorwonen-reactor:latest .
# docker push $ACR_NAME.azurecr.io/klikvoorwonen-reactor:latest
```

### Step 4: Create Container App Scheduled Job

```bash
# Get ACR credentials
ACR_USERNAME=$(az acr credential show --name $ACR_NAME --query username -o tsv)
ACR_PASSWORD=$(az acr credential show --name $ACR_NAME --query passwords[0].value -o tsv)

# Create the scheduled job
az containerapp job create \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --environment $ENVIRONMENT_NAME \
  --trigger-type "Schedule" \
  --cron-expression "0 9 * * *" \
  --replica-timeout 1800 \
  --replica-retry-limit 1 \
  --parallelism 1 \
  --replica-completion-count 1 \
  --image $ACR_NAME.azurecr.io/klikvoorwonen-reactor:latest \
  --cpu 0.5 \
  --memory 1Gi \
  --registry-server $ACR_NAME.azurecr.io \
  --registry-username $ACR_USERNAME \
  --registry-password $ACR_PASSWORD \
  --secrets \
    klikvoorwonen-username="YOUR_USERNAME" \
    klikvoorwonen-password="YOUR_PASSWORD" \
  --env-vars \
    KLIKVOORWONEN_USERNAME=secretref:klikvoorwonen-username \
    KLIKVOORWONEN_PASSWORD=secretref:klikvoorwonen-password \
    HEADLESS=true
```

**Replace `YOUR_USERNAME` and `YOUR_PASSWORD` with your actual Klik voor Wonen credentials!**

### Step 5: Test the Job Manually

```bash
# Start the job manually to test
az containerapp job start \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP

# View logs
az containerapp job execution list \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --output table

# Get the execution name from the list above, then view logs:
EXECUTION_NAME="<execution-name-from-above>"
az containerapp logs show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --type system
```

## 🔄 Updating the Application

When you make changes to the code:

```bash
# Rebuild and push the image
az acr build \
  --registry $ACR_NAME \
  --image klikvoorwonen-reactor:latest \
  --file Dockerfile \
  .

# Update the container app job to use the new image
az containerapp job update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --image $ACR_NAME.azurecr.io/klikvoorwonen-reactor:latest
```

## 📊 Monitoring

View logs in Azure Portal:
1. Go to your Container App Job
2. Click on "Logs" in the left menu
3. View execution history and console output

Or use Azure CLI:
```bash
# List executions
az containerapp job execution list \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP

# View logs for specific execution
az containerapp logs show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP
```

## 🔧 Customization

### Change Schedule

Edit the cron expression:
- `0 9 * * *` - Every day at 9:00 AM
- `0 */6 * * *` - Every 6 hours
- `0 9,21 * * *` - Every day at 9:00 AM and 9:00 PM

```bash
az containerapp job update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --cron-expression "0 9,21 * * *"
```

### Update Credentials

```bash
az containerapp job update \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --secrets \
    klikvoorwonen-username="NEW_USERNAME" \
    klikvoorwonen-password="NEW_PASSWORD"
```

## 🐛 Troubleshooting

### Selectors Not Working

The website structure may have changed. You need to:
1. Run locally with `HEADLESS=false`
2. Open browser developer tools (F12)
3. Inspect the login form and reaction buttons
4. Update the CSS selectors in `Program.cs`

### Login Failing

- Check if Klik voor Wonen has captcha (if so, this won't work)
- Verify credentials are correct
- Check if the site blocks automated logins
- Look at the screenshot in logs to see what went wrong

### Job Not Running

```bash
# Check job status
az containerapp job show \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --query "properties.configuration.triggerConfig"

# Manually trigger to test
az containerapp job start \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP
```

## 💰 Cost Estimate

- **Azure Container Registry (Basic)**: ~€4.25/month
- **Container Apps Environment**: Free
- **Container Apps Job**: Free (within 180,000 vCPU-seconds/month)

**Total**: ~€4.25/month

## 🗑️ Cleanup

To delete everything:

```bash
# Delete just the Container App Job
az containerapp job delete \
  --name $APP_NAME \
  --resource-group $RESOURCE_GROUP \
  --yes

# Or delete the entire resource group (removes everything)
az group delete \
  --name $RESOURCE_GROUP \
  --yes
```

## 📝 Notes

- The automation respects rate limits by waiting 1 second between reactions
- Screenshots are taken for debugging but not persisted
- Logs are available for 30 days in Azure
- The job has a 30-minute timeout (configurable)

## ⚠️ Disclaimer

This automation is for personal use only. Make sure using automation complies with Klik voor Wonen's terms of service.
