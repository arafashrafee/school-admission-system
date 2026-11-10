# Build stage
FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

# Copy project file and restore dependencies
COPY SchoolAdmissionSystem.Api.csproj ./
RUN dotnet restore SchoolAdmissionSystem.Api.csproj

# Copy the rest of the source code and build
COPY . ./
RUN dotnet publish SchoolAdmissionSystem.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

# Runtime stage
FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS runtime
WORKDIR /app

# Install curl for healthcheck support
RUN apt-get update \
    && apt-get install -y --no-install-recommends curl \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_HTTP_PORTS=8080
ENV ASPNETCORE_ENVIRONMENT=Development
EXPOSE 8080

COPY --from=build /app/publish ./
ENTRYPOINT ["dotnet", "SchoolAdmissionSystem.Api.dll"]
