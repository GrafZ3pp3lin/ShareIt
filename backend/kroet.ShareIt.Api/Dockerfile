FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY --link "kroet.Permissions.AspNetCore/kroet.Permissions.AspNetCore.csproj" "kroet.Permissions.AspNetCore/"
COPY --link "kroet.Permissions/kroet.Permissions.csproj" "kroet.Permissions/"
COPY --link "kroet.ShareIt.Api/kroet.ShareIt.Api.csproj" "kroet.ShareIt.Api/"

RUN dotnet restore "kroet.ShareIt.Api/kroet.ShareIt.Api.csproj"

COPY --link . .

RUN dotnet publish "./kroet.ShareIt.Api.csproj" -c $BUILD_CONFIGURATION -o /app/publish /p:UseAppHost=false --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final

USER $APP_UID
WORKDIR /app
EXPOSE 8080

COPY --link --from=build /app/publish .

ENTRYPOINT ["dotnet", "kroet.ShareIt.Api.dll"]
