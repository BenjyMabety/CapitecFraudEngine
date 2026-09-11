# -----------------------------------------
# STAGE 1: Build the application
# -----------------------------------------
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy the project file and restore dependencies
COPY ["CapitecFraudEngine.csproj", "./"]
RUN dotnet restore "CapitecFraudEngine.csproj"

# Copy the remaining source code
COPY . .

# Build and publish the application to the /app/publish directory
RUN dotnet publish "CapitecFraudEngine.csproj" -c Release -o /app/publish /p:UseAppHost=false

# -----------------------------------------
# STAGE 2: Create the runtime image
# -----------------------------------------
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

# Install tzdata so Linux recognizes timezones
RUN apt-get update && apt-get install -y tzdata && rm -rf /var/lib/apt/lists/*
ENV TZ=Africa/Johannesburg

# Copy the compiled binaries from the build stage
COPY --from=build /app/publish .

# Expose the standard ASP.NET Core port
EXPOSE 8080

# Start the application
ENTRYPOINT ["dotnet", "CapitecFraudEngine.dll"]