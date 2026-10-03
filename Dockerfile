FROM mcr.microsoft.com/dotnet/sdk:8.0 AS build
WORKDIR /src

COPY ["MotoRental/MotoRental.csproj", "MotoRental/"]
RUN dotnet restore "MotoRental/MotoRental.csproj"

COPY . .
WORKDIR "/src/MotoRental"
RUN dotnet publish "MotoRental.csproj" -c Release -o /app/publish /p:UseAppHost=false

FROM mcr.microsoft.com/dotnet/aspnet:8.0 AS final
WORKDIR /app
COPY --from=build /app/publish .

ENV ASPNETCORE_URLS=http://+:8080
EXPOSE 8080

ENTRYPOINT ["dotnet", "MotoRental.dll"]
