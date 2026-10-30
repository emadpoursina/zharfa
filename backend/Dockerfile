# Zharfa backend image — ASP.NET Core Web API (net10.0)
FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY backend/Zharfa.Api/Zharfa.Api.csproj backend/Zharfa.Api/
RUN dotnet restore backend/Zharfa.Api/Zharfa.Api.csproj

COPY backend/ backend/
RUN dotnet publish backend/Zharfa.Api/Zharfa.Api.csproj -c Release -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
COPY --from=build /app/publish .
EXPOSE 8080
ENTRYPOINT ["dotnet", "Zharfa.Api.dll"]
