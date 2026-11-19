FROM mcr.microsoft.com/dotnet/sdk:11.0-resolute AS build
ARG BUILD_CONFIGURATION=Release
WORKDIR /src

# Central package management must be visible before restore.
COPY --link "Directory.Packages.props" .
COPY --link "kroet.ShareIt.Api/kroet.ShareIt.Api.csproj" "kroet.ShareIt.Api/"

RUN dotnet restore "kroet.ShareIt.Api/kroet.ShareIt.Api.csproj"

# Only the API project is needed for the build; AuthTwin and the test projects
# never enter the Docker context (see backend/.dockerignore).
COPY --link "kroet.ShareIt.Api/" "kroet.ShareIt.Api/"

# The release version ends up in the assembly informational version, which
# ObservabilityExtensions.ResolveServiceVersion() reports to OpenTelemetry
# (shareit_build_info metric, trace resource attribute).
ARG VERSION=0.0.0
RUN dotnet publish "kroet.ShareIt.Api/kroet.ShareIt.Api.csproj" \
    -c $BUILD_CONFIGURATION \
    -o /app/publish \
    /p:UseAppHost=false \
    /p:Version=$VERSION \
    --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:11.0-resolute-chiseled AS final
WORKDIR /app
EXPOSE 8080
ENV ASPNETCORE_URLS=http://+:8080

USER $APP_UID
COPY --link --from=build /app/publish .

ENTRYPOINT ["dotnet", "kroet.ShareIt.Api.dll"]
