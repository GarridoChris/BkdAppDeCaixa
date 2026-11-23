FROM mcr.microsoft.com/dotnet/sdk:10.0 AS build
WORKDIR /src

COPY src/AppDeCaixa.Api/AppDeCaixa.Api.csproj src/AppDeCaixa.Api/
COPY src/AppDeCaixa.Application/AppDeCaixa.Application.csproj src/AppDeCaixa.Application/
COPY src/AppDeCaixa.Domain/AppDeCaixa.Domain.csproj src/AppDeCaixa.Domain/
COPY src/AppDeCaixa.Infrastructure/AppDeCaixa.Infrastructure.csproj src/AppDeCaixa.Infrastructure/
RUN dotnet restore src/AppDeCaixa.Api/AppDeCaixa.Api.csproj

COPY src/ src/
RUN dotnet publish src/AppDeCaixa.Api/AppDeCaixa.Api.csproj -c Release --no-restore -o /app/publish

FROM mcr.microsoft.com/dotnet/aspnet:10.0 AS runtime
WORKDIR /app
ENV ASPNETCORE_HTTP_PORTS=8080
EXPOSE 8080
COPY --from=build /app/publish/ ./
USER $APP_UID
CMD ["dotnet", "/app/AppDeCaixa.Api.dll"]
