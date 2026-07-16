FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY Directory.Build.props ./
COPY Sassoir.Api/Sassoir.Api.csproj Sassoir.Api/
RUN dotnet restore Sassoir.Api/Sassoir.Api.csproj

COPY Sassoir.Api/ Sassoir.Api/
RUN dotnet publish Sassoir.Api/Sassoir.Api.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app

ENV ASPNETCORE_ENVIRONMENT=Production
COPY --from=build /app/publish ./

ENTRYPOINT ["dotnet", "Sassoir.Api.dll"]
