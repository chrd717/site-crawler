FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src
COPY . .
RUN dotnet restore BrightCrawler.sln
RUN dotnet publish src/BrightCrawler.App/BrightCrawler.App.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/runtime:10.0
WORKDIR /app
COPY --from=build /app/publish .
COPY src/BrightCrawler.App/appsettings.json .
ENTRYPOINT ["dotnet", "BrightCrawler.App.dll"]
