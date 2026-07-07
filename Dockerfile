FROM mcr.microsoft.com/dotnet/sdk:9.0 AS build
WORKDIR /src

COPY TechSupportRagBot.sln ./
COPY TechSupportRagBot/TechSupportRagBot.csproj TechSupportRagBot/
RUN dotnet restore TechSupportRagBot/TechSupportRagBot.csproj

COPY . .
RUN dotnet publish TechSupportRagBot/TechSupportRagBot.csproj -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0 AS runtime
WORKDIR /app

RUN apt-get update \
    && apt-get install -y --no-install-recommends ffmpeg \
    && rm -rf /var/lib/apt/lists/*

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

COPY --from=build /app/publish .
ENTRYPOINT ["dotnet", "TechSupportRagBot.dll"]
