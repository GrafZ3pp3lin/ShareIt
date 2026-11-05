FROM mcr.microsoft.com/dotnet/sdk:10.0-noble AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY --link "Directory.Packages.props" .
COPY --link "kroet.Permission.AspNetCore/kroet.Permission.AspNetCore.csproj" "kroet.Permission.AspNetCore/"
COPY --link "kroet.Permissions/kroet.Permissions.csproj" "kroet.Permissions/"
COPY --link "kroet.ShareIt.Api/kroet.ShareIt.Api.csproj" "kroet.ShareIt.Api/"

RUN dotnet restore "kroet.ShareIt.Api/kroet.ShareIt.Api.csproj"

COPY --link . .

RUN dotnet publish "kroet.ShareIt.Api/kroet.ShareIt.Api.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0-noble-chiseled AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

USER $APP_UID
COPY --link --from=build /app/publish .

ENTRYPOINT ["dotnet", "kroet.ShareIt.Api.dll"]
