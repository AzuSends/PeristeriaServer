FROM mcr.microsoft.com/dotnet/runtime:10.0 AS runtime
WORKDIR /app

FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src
COPY ["PeristeriaServer/PeristeriaServer.csproj", "PeristeriaServer/"]
RUN dotnet restore "PeristeriaServer/PeristeriaServer.csproj"
COPY . .
WORKDIR "/src/PeristeriaServer"
RUN dotnet build "./PeristeriaServer.csproj" -c $BUILD_CONFIGURATION -o /app/build

FROM build AS publish
ARG BUILD_CONFIGURATION=Release
RUN dotnet publish "./PeristeriaServer.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false

FROM runtime AS final
WORKDIR /app
COPY --from=publish /app/publish .
ENTRYPOINT ["dotnet", "PeristeriaServer.dll"]
