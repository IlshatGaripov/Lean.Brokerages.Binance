### This container is used to build projects  ###

FROM mcr.microsoft.com/dotnet/sdk:6.0 as build-env
COPY /Lean /Lean
WORKDIR /app
COPY /Lean.Brokerages.Binance .
WORKDIR /app/QuantConnect.BinanceBrokerage.ToolBox

RUN dotnet restore
RUN dotnet publish -c Release -o out

### Runtime container ###

FROM mcr.microsoft.com/dotnet/runtime:6.0
WORKDIR /app
COPY --from=build-env /app/QuantConnect.BinanceBrokerage.ToolBox/out .
ENV ENVIRONMENT=Docker

ENTRYPOINT ["dotnet", "QuantConnect.BinanceBrokerage.ToolBox.dll"]
