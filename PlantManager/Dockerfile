FROM mcr.microsoft.com/dotnet/sdk:9.0 AS publish
WORKDIR /src

COPY ["PlantManager/PlantManager.csproj", "PlantManager/"]
RUN dotnet restore "PlantManager/PlantManager.csproj"

COPY . .
WORKDIR "/src/PlantManager"
RUN dotnet publish "PlantManager.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:9.0
WORKDIR /app
COPY --from=publish /app/publish .

EXPOSE 5000
ENV ASPNETCORE_URLS=http://+:5000
ENV ASPNETCORE_ENVIRONMENT=Development

ENTRYPOINT ["dotnet", "PlantManager.dll"]