FROM mcr.microsoft.com/dotnet/sdk:8.0-alpine AS build
WORKDIR /src
COPY TreasuryLabelVerifier.sln ./
COPY src/TreasuryLabelVerifier.Web/TreasuryLabelVerifier.Web.csproj src/TreasuryLabelVerifier.Web/
COPY tests/TreasuryLabelVerifier.Tests/TreasuryLabelVerifier.Tests.csproj tests/TreasuryLabelVerifier.Tests/
RUN dotnet restore TreasuryLabelVerifier.sln
COPY . .
RUN dotnet publish src/TreasuryLabelVerifier.Web/TreasuryLabelVerifier.Web.csproj \
    --configuration Release --no-restore --output /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:8.0-alpine AS final
WORKDIR /app
RUN addgroup --system appgroup && adduser --system --ingroup appgroup appuser
COPY --from=build --chown=appuser:appgroup /app/publish .
USER appuser
ENV ASPNETCORE_URLS=http://+:8080 \
    ASPNETCORE_ENVIRONMENT=Production
EXPOSE 8080
ENTRYPOINT ["dotnet", "TreasuryLabelVerifier.Web.dll"]
