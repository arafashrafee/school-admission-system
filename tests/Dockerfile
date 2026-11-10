FROM mcr.microsoft.com/dotnet/sdk:8.0
WORKDIR /src
COPY backend/SchoolAdmissionSystem.Api.csproj backend/
COPY tests/SchoolAdmissionSystem.Tests/SchoolAdmissionSystem.Tests.csproj tests/SchoolAdmissionSystem.Tests/
RUN dotnet restore tests/SchoolAdmissionSystem.Tests/SchoolAdmissionSystem.Tests.csproj
COPY SchoolAdmissionSystem.sln ./
COPY backend/ backend/
COPY tests/SchoolAdmissionSystem.Tests/ tests/SchoolAdmissionSystem.Tests/
ENTRYPOINT ["dotnet", "test", "tests/SchoolAdmissionSystem.Tests/SchoolAdmissionSystem.Tests.csproj", "--no-restore", "--logger", "console;verbosity=normal"]
