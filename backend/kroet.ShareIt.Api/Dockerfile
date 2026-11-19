FROM mcr.microsoft.com/dotnet/sdk:11.0-resolute AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

COPY --link "Directory.Packages.props" .
COPY --link "kroet.ShareIt.Api/kroet.ShareIt.Api.csproj" "kroet.ShareIt.Api/"

RUN dotnet restore "kroet.ShareIt.Api/kroet.ShareIt.Api.csproj"

COPY --link . .

RUN dotnet publish "kroet.ShareIt.Api/kroet.ShareIt.Api.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

USER $APP_UID
COPY --link --from=build /app/publish .

ENTRYPOINT ["dotnet", "kroet.ShareIt.Api.dll"]
