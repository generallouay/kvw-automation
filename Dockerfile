# Use Playwright's official .NET image which includes browsers and dependencies
FROM mcr.microsoft.com/playwright/dotnet:v1.49.0-jammy

# Set working directory
WORKDIR /app

# Copy project files
COPY *.csproj ./
RUN dotnet restore

# Copy everything else
COPY . ./

# Build the application
RUN dotnet publish -c Release -o out

# Set the working directory to the output
WORKDIR /app/out

# Run the application
ENTRYPOINT ["dotnet", "KlikVoorWonen.dll"]
