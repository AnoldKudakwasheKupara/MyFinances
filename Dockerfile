FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY MyFinances/MyFinances.csproj MyFinances/
RUN dotnet restore MyFinances/MyFinances.csproj

COPY MyFinances/ MyFinances/
RUN dotnet publish MyFinances/MyFinances.csproj -c Release -o /app --no-restore

FROM mcr.microsoft.com/dotnet/aspnet:10.0

RUN apt-get update \
 && DEBIAN_FRONTEND=noninteractive apt-get install -y --no-install-recommends tzdata \
 && rm -rf /var/lib/apt/lists/*

WORKDIR /app
COPY --from=build /app .

ENV ASPNETCORE_ENVIRONMENT=Production \
    TZ=Africa/Harare

EXPOSE 10000

CMD ["sh", "-c", "ASPNETCORE_HTTP_PORTS=${PORT:-10000} exec dotnet MyFinances.dll"]
